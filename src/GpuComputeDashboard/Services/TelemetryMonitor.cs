using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using GpuComputeDashboard.Models;
using Microsoft.Extensions.Options;

namespace GpuComputeDashboard.Services;

/// <summary>Samples host and GPU telemetry locally; preserves a small in-memory history.</summary>
public sealed class TelemetryMonitor(ProcessRunner runner, IOptions<DashboardOptions> settings, ILogger<TelemetryMonitor> logger) : BackgroundService
{
    private readonly DashboardOptions options = settings.Value;
    private readonly object gate = new();
    private readonly Queue<TelemetryPoint> history = new();
    private TelemetrySnapshot latest = new(DateTimeOffset.MinValue, false,
        new("unknown", "Linux", 0, null, null, null, null, null, TimeSpan.Zero), null);
    private (long Total, long Idle)? previousCpu;

    public TelemetrySnapshot Current { get { lock (gate) return latest; } }
    public IReadOnlyList<TelemetryPoint> History { get { lock (gate) return history.ToArray(); } }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sample = options.DemoMode ? CreateDemo() : await ReadRealAsync(stoppingToken);
                lock (gate)
                {
                    latest = sample;
                    history.Enqueue(new(sample.Timestamp, sample.Gpu?.Utilization, sample.Gpu?.MemoryPercent,
                        sample.Host.CpuPercent, sample.Gpu?.TemperatureC));
                    while (history.Count > 360) history.Dequeue();
                }
            }
            catch (Exception ex) { logger.LogWarning(ex, "Telemetry sampling failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.PollSeconds, 2, 60)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private TelemetrySnapshot CreateDemo()
    {
        var now = DateTimeOffset.UtcNow;
        var wave = Math.Sin(now.ToUnixTimeSeconds() / 31.0);
        return new(now, true,
            new(options.NodeName, "Ubuntu 24.04.4 LTS", 24, 38 + wave * 5, 17.9, 32, 54, 128, TimeSpan.FromDays(12.17)),
            new("Tesla V100-PCIE-32GB", "NVIDIA", "580.178.04", 72 + wave * 8,
                23.1, 32, 68 + wave * 2, 212 + wave * 14, 1290, 877, "Demo"));
    }

    private async Task<TelemetrySnapshot> ReadRealAsync(CancellationToken token)
    {
        GpuSnapshot? gpu = await QueryNvidiaAsync(token) ?? await QueryAmdAsync(token);
        return new(DateTimeOffset.UtcNow, false, ReadHost(), gpu);
    }

    private HostSnapshot ReadHost()
    {
        double? cpu = null;
        try
        {
            var parts = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var values = parts.Skip(1).Select(long.Parse).ToArray();
            var total = values.Sum();
            var idle = values[3] + (values.Length > 4 ? values[4] : 0);
            if (previousCpu is { } previous && total > previous.Total)
                cpu = Math.Clamp(100d * (1 - (double)(idle - previous.Idle) / (total - previous.Total)), 0, 100);
            previousCpu = (total, idle);
        }
        catch { /* /proc unavailable; expose an unknown value rather than inventing one */ }

        double? memTotal = null, memUsed = null;
        try
        {
            var info = File.ReadAllLines("/proc/meminfo").Select(line => line.Split(':', 2))
                .Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
            var totalKb = long.Parse(info["MemTotal"].Trim().Split(' ')[0], CultureInfo.InvariantCulture);
            var availableKb = long.Parse(info["MemAvailable"].Trim().Split(' ')[0], CultureInfo.InvariantCulture);
            memTotal = totalKb / 1048576d;
            memUsed = (totalKb - availableKb) / 1048576d;
        }
        catch { }

        double? diskTotal = null, diskUsed = null;
        try
        {
            var disk = new DriveInfo("/");
            if (disk.IsReady) { diskTotal = disk.TotalSize / 1073741824d; diskUsed = (disk.TotalSize - disk.AvailableFreeSpace) / 1073741824d; }
        }
        catch { }

        TimeSpan uptime = TimeSpan.Zero;
        try { uptime = TimeSpan.FromSeconds(double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0], CultureInfo.InvariantCulture)); }
        catch { }
        return new(Environment.MachineName, RuntimeInformation.OSDescription, Environment.ProcessorCount, cpu,
            memUsed, memTotal, diskUsed, diskTotal, uptime);
    }

    private static double? Number(string? value)
    {
        if (value is null || value.Trim().Equals("N/A", StringComparison.OrdinalIgnoreCase)) return null;
        var found = Regex.Match(value, @"[-+]?[0-9]+(?:\.[0-9]+)?");
        return found.Success && double.TryParse(found.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private async Task<GpuSnapshot?> QueryNvidiaAsync(CancellationToken token)
    {
        var result = await runner.RunAsync("nvidia-smi", [
            "--query-gpu=name,driver_version,utilization.gpu,memory.used,memory.total,temperature.gpu,power.draw,clocks.current.graphics,clocks.current.memory,uuid",
            "--format=csv,noheader,nounits"], token, 4);
        if (!result.Success) return null;
        var line = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (line is null) return null;
        var fields = line.Split(',').Select(x => x.Trim()).ToArray();
        if (fields.Length < 10) return null;
        return new(fields[0], "NVIDIA", fields[1], Number(fields[2]),
            Number(fields[3]) / 1024d, Number(fields[4]) / 1024d, Number(fields[5]),
            Number(fields[6]), Number(fields[7]), Number(fields[8]), "nvidia-smi",
            ComputeProcesses: await QueryNvidiaProcessesAsync(fields[9], token));
    }

    private async Task<IReadOnlyList<GpuProcess>?> QueryNvidiaProcessesAsync(string gpuUuid, CancellationToken token)
    {
        var result = await runner.RunAsync("nvidia-smi", [
            "--id=" + gpuUuid, "--query-compute-apps=pid,process_name,used_gpu_memory",
            "--format=csv,noheader,nounits"], token, 4);
        if (!result.Success) return null;

        var processes = new List<GpuProcess>();
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var firstComma = line.IndexOf(',');
            var lastComma = line.LastIndexOf(',');
            if (firstComma < 0 || firstComma == lastComma ||
                !int.TryParse(line.AsSpan(0, firstComma), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ||
                pid <= 0)
                continue;

            var name = line[(firstComma + 1)..lastComma].Trim();
            long? memory = long.TryParse(line.AsSpan(lastComma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var usedMiB) && usedMiB >= 0 ? usedMiB : null;
            processes.Add(new(pid, name, memory, ReadProcessControlGroup(pid)));
        }
        return processes;
    }

    private static string? ReadProcessControlGroup(int pid)
    {
        try
        {
            // Use only the unified hierarchy. The process may exit before this read.
            var entry = File.ReadLines($"/proc/{pid}/cgroup")
                .FirstOrDefault(line => line.StartsWith("0::/", StringComparison.Ordinal));
            return entry?[3..];
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    // Legacy ROCm SMI is used on MI50 installations; output fields vary across driver versions.
    // A metric is only shown when an identifiable key exists.
    private async Task<GpuSnapshot?> QueryAmdAsync(CancellationToken token)
    {
        var result = await runner.RunAsync("rocm-smi", [
            "--showproductname", "--showuse", "--showmemuse", "--showtemp", "--showpower", "--json"], token, 4);
        if (!result.Success) return null;
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Flatten(document.RootElement, properties);
            string? Find(params string[] names) => properties.FirstOrDefault(kv =>
                names.Any(name => kv.Key.Contains(name, StringComparison.OrdinalIgnoreCase))).Value;
            var name = Find("Card series", "GPU ID") ?? "AMD Instinct";
            var usage = Number(Find("GPU use (%)", "GPU Utilization"));
            var memoryPct = Number(Find("GPU memory use (%)", "VRAM Usage"));
            var temp = Number(Find("Temperature (Sensor edge)", "Temperature (Sensor junction)", "Temperature (C)"));
            var watts = Number(Find("Average Graphics Package Power", "Power (W)"));
            return new(name, "AMD", "ROCm SMI", usage,
                null, null, temp, watts, null, null, "rocm-smi",
                memoryPct is not null ? "VRAM percentage available from ROCm SMI; byte counts not supported by this adapter yet." : null);
        }
        catch (JsonException) { return null; }
    }

    private static void Flatten(JsonElement element, Dictionary<string, string> output, string prefix = "")
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) Flatten(property.Value, output, prefix + "/" + property.Name);
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) Flatten(item, output, prefix);
        else if (element.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            output[prefix] = element.ToString();
    }
}
