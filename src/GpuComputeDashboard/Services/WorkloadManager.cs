using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using GpuComputeDashboard.Models;
using Microsoft.Extensions.Options;

namespace GpuComputeDashboard.Services;

/// <summary>Only explicitly configured units are inspected and controlled.</summary>
public sealed class WorkloadManager(ProcessRunner runner, IOptions<DashboardOptions> settings)
{
    private readonly DashboardOptions options = settings.Value;
    private readonly ConcurrentDictionary<string, bool> demoRunning = new();
    private static readonly Regex SafeUnit = new(@"^[A-Za-z0-9][A-Za-z0-9_.@-]*\.service$", RegexOptions.Compiled);

    public DashboardOptions Options => options;

    public async Task<IReadOnlyList<WorkloadState>> GetAllAsync(CancellationToken token = default)
    {
        var tasks = options.Workloads.Select(workload => GetStateAsync(workload, token));
        return await Task.WhenAll(tasks);
    }

    public async Task<WorkloadState?> GetAsync(string id, CancellationToken token = default)
    {
        var definition = options.Workloads.FirstOrDefault(w => w.Id == id);
        return definition is null ? null : await GetStateAsync(definition, token);
    }

    private async Task<WorkloadState> GetStateAsync(WorkloadDefinition workload, CancellationToken token)
    {
        WorkloadState State(string state, string sub = "", int? pid = null, double? memory = null, string? error = null)
            => new(workload.Id, workload.Name, workload.Unit, workload.Scope, workload.Category,
                workload.Description, workload.Url, workload.Path, workload.Port, state, sub, pid, memory, error, workload.ShowInApplications);

        if (options.DemoMode)
            return State(demoRunning.GetOrAdd(workload.Id, true) ? "active" : "inactive", "demo",
                demoRunning[workload.Id] ? 1200 + Math.Abs(workload.Id.GetHashCode() % 5000) : null);
        if (!Valid(workload)) return State("error", error: "Invalid or unconfigured systemd unit.");
        var args = UnitArgs(workload, "show");
        args.AddRange(["--property=LoadState,ActiveState,SubState,MainPID,MemoryCurrent", "--no-pager"]);
        var result = await runner.RunAsync("systemctl", args, token);
        if (!result.Success) return State("unavailable", error: result.Error.Trim() is { Length: > 0 } e ? e : result.Output.Trim());
        var fields = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
        var value = (string key) => fields.GetValueOrDefault(key, "");
        if (value("LoadState") == "not-found") return State("not-found", error: "Unit not found; verify the configured service name.");
        int? pid = int.TryParse(value("MainPID"), out var parsedPid) && parsedPid > 0 ? parsedPid : null;
        double? memory = long.TryParse(value("MemoryCurrent"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var bytes) && bytes >= 0 && bytes < long.MaxValue
                ? bytes / 1048576d : null;
        return State(value("ActiveState") is { Length: > 0 } active ? active : "unknown",
            value("SubState"), pid, memory);
    }

    public async Task<OperationResult> ControlAsync(string id, string action, CancellationToken token = default)
    {
        if (action is not ("start" or "stop" or "restart")) return new(false, "Unsupported operation.");
        var workload = options.Workloads.FirstOrDefault(w => w.Id == id);
        if (workload is null || !Valid(workload)) return new(false, "Unknown or invalid configured service.");
        if (options.DemoMode)
        {
            demoRunning[workload.Id] = action != "stop";
            return new(true, "Demo: " + workload.Name + " " + action + " simulated; host unchanged.");
        }
        if (!options.EnableServiceControls)
            return new(false, "Service controls are disabled. Set Dashboard:EnableServiceControls to true after configuring authorization.");
        var args = UnitArgs(workload, action);
        var result = await runner.RunAsync("systemctl", args, token, 20);
        return new(result.Success, result.Success
            ? workload.Name + ": " + action + " requested."
            : "systemctl: " + (string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim()));
    }

    public async Task<string> ReadJournalAsync(string id, int lines = 80, CancellationToken token = default)
    {
        var workload = options.Workloads.FirstOrDefault(w => w.Id == id);
        if (workload is null || !Valid(workload)) return "Select a registered service.";
        if (options.DemoMode) return "[demo] " + workload.Name + " — no real journal data is connected.";
        var args = new List<string>();
        if (workload.Scope == "user") args.Add("--user");
        args.AddRange(["--unit=" + workload.Unit, "--lines=" + Math.Clamp(lines, 1, 200), "--no-pager", "--output=short-iso"]);
        var result = await runner.RunAsync("journalctl", args, token);
        return result.Success ? result.Output : "journalctl: " + result.Error.Trim();
    }

    public IReadOnlyList<MountState> GetMounts()
    {
        if (options.DemoMode)
            return options.Mounts.Select(m => new MountState(m.Path, m.Source, true, m.Source, "nfs4 (demo)")).ToArray();
        var mounts = new Dictionary<string, (string Source, string Type)>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 3)
                    mounts[fields[1].Replace(@"\040", " ")] = (fields[0], fields[2]);
            }
        }
        catch { }
        return options.Mounts.Select(m =>
        {
            mounts.TryGetValue(m.Path, out var actual);
            var mounted = actual.Source is not null &&
                (m.Source is null || string.Equals(actual.Source, m.Source, StringComparison.Ordinal));
            return new MountState(m.Path, m.Source, mounted, actual.Source, actual.Type);
        }).ToArray();
    }

    private static bool Valid(WorkloadDefinition workload) =>
        (workload.Scope is "system" or "user") && SafeUnit.IsMatch(workload.Unit);

    private static List<string> UnitArgs(WorkloadDefinition w, string verb)
    {
        var args = new List<string>();
        if (w.Scope == "user") args.Add("--user");
        args.Add(verb);
        args.Add(w.Unit);
        return args;
    }
}
