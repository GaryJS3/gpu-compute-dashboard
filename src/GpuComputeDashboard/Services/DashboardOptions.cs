namespace GpuComputeDashboard.Services;

public sealed class DashboardOptions
{
    public string NodeName { get; set; } = "ai4-v100";
    public string GpuLabel { get; set; } = "NVIDIA Tesla V100 32 GB";
    public string OsLabel { get; set; } = "Ubuntu Linux";
    public bool DemoMode { get; set; }
    public bool EnableServiceControls { get; set; }
    public int PollSeconds { get; set; } = 5;
    public List<WorkloadDefinition> Workloads { get; set; } = [];
    public List<MountDefinition> Mounts { get; set; } = [];
}
public sealed class WorkloadDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Scope { get; set; } = "system";
    public string Category { get; set; } = "Service";
    public string Description { get; set; } = "";
    public string? Url { get; set; }
    public string? Path { get; set; }
    public int? Port { get; set; }
}
public sealed class MountDefinition
{
    public string Path { get; set; } = "";
    public string? Source { get; set; }
}
