# Codex instructions

This is a local-per-VM .NET 10 Blazor Interactive Server GPU dashboard, based on Command Center design 01. It targets Ubuntu with passed-through GPUs, especially ai4-v100.

Read README.md and docs/CODEX-HANDOFF.md first. Preserve the dark dashboard visual language. Favor C#, Razor, and systemd over Python or YAML when implementing application features.

Safety:
- Do not grant arbitrary shell execution, allow command arguments from the browser, or run the web app as root.
- The only mutating service operations are start/stop/restart against exact allowlisted systemd units. Real controls are off by default.
- The app has no built-in authentication yet. Keep Kestrel loopback-only until authentication and authorization are implemented.
- Do not overwrite existing Python environments, uncommitted project files, or systemd units.
- Do not pretend to attribute GPU memory to a workload without verifying process/cgroup ownership.
- Sample/demo data must remain conspicuously labeled.

Run `dotnet build src/GpuComputeDashboard/GpuComputeDashboard.csproj` after changes. CI builds on push/PR. If using a V100 VM, collect actual unit names and hardware telemetry before enabling control.
