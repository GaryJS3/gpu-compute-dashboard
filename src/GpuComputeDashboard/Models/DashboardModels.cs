namespace GpuComputeDashboard.Models;

public sealed record GpuSnapshot(
    string Name, string Vendor, string Driver, double? Utilization,
    double? MemoryUsedGb, double? MemoryTotalGb, double? TemperatureC,
    double? PowerWatts, double? CoreMhz, double? MemoryMhz,
    string Backend, string? Error = null)
{
    public double? MemoryPercent => MemoryTotalGb > 0 ? 100 * MemoryUsedGb / MemoryTotalGb : null;
}

public sealed record HostSnapshot(
    string Hostname, string OsDescription, int CpuCount, double? CpuPercent,
    double? MemoryUsedGb, double? MemoryTotalGb, double? DiskUsedGb,
    double? DiskTotalGb, TimeSpan Uptime)
{
    public double? MemoryPercent => MemoryTotalGb > 0 ? MemoryUsedGb * 100 / MemoryTotalGb : null;
    public double? DiskPercent => DiskTotalGb > 0 ? DiskUsedGb * 100 / DiskTotalGb : null;
}

public sealed record TelemetrySnapshot(DateTimeOffset Timestamp, bool IsDemo, HostSnapshot Host, GpuSnapshot? Gpu);
public sealed record TelemetryPoint(DateTimeOffset Timestamp, double? GpuPercent, double? VramPercent, double? CpuPercent, double? Temperature);

public sealed record WorkloadState(
    string Id, string Name, string Unit, string Scope, string Category,
    string Description, string? Url, string? Path, int? Port, string State,
    string SubState, int? Pid, double? MemoryMb, string? Error = null, bool ShowInApplications = true,
    TimeSpan? CpuTime = null, int? Tasks = null)
{
    public bool Running => State == "active";
    public bool Missing => State == "not-found";
}
public sealed record OperationResult(bool Success, string Message);
public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;
}
public sealed record MountState(string Path, string? ExpectedSource, bool Mounted, string? ActualSource, string? Filesystem);
