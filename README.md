# VectorGPU — GPU Compute Dashboard

Per-VM compute dashboard built with **ASP.NET Core 10 / Blazor Interactive Server**. Designed for Ubuntu GPU passthrough guests. The `ai4-v100` branch carries the inventory and deployment settings for the first VM.

The UI takes its direction from the **Command Center 01** HTML concept: dark sidebar, GPU KPI cards, charts, workload table, application cards, journal, mounts, and settings.

## Current capabilities

- Live **Linux host** sampling: CPU from `/proc/stat`, available RAM from `/proc/meminfo`, root filesystem and uptime.
- Live **NVIDIA GPU** sampling through `nvidia-smi` (utilization, VRAM, temperature, power, clocks, driver).
- Experimental **legacy ROCm SMI** detection for AMD nodes, with unavailable metrics clearly marked.
- Rolling telemetry chart (in-memory, not persisted).
- Inspect explicitly registered **systemd system and user services**: state, main PID, unit memory.
- Start/stop/restart configured units, with confirmation, **disabled on real machines by default**.
- Per-unit journal output, registered NFS mount validation, application launch links.
- Development demonstration mode with sample V100 values and simulated controls.

**Not implemented:** automatic service discovery/adoption, Docker management, GPU per-process VRAM attribution, persisted metrics, model management, configuration editing, centralized orchestration, per-user accounts or backup/migration automation. These are follow-on tasks.

## Run

.NET 10 SDK is required.

```bash
cd src/GpuComputeDashboard
dotnet run
```

The included launch profile sets Development mode, which uses sample data. To bypass the launch profile, set ASPNETCORE_ENVIRONMENT explicitly. Open the local URL printed by ASP.NET Core (configured default **http://127.0.0.1:5088**).

To test real telemetry locally:

```bash
ASPNETCORE_ENVIRONMENT=Production dotnet run --no-launch-profile
```

Production settings enable real monitoring and disable service changes. The web server binds to localhost by default. From another computer, use `ssh -L 5088:127.0.0.1:5088 USER@VM`, then open **http://127.0.0.1:5088** on your workstation.

## Configure a node

Define `Dashboard:Workloads` and `Dashboard:Mounts` in `appsettings.Production.json` using the development configuration as a schema example. Set a workload's `ShowInApplications` to `false` for a supporting service that should stay in Workloads and Journal without appearing in the application launcher. Verify service unit names with `systemctl list-unit-files --type=service` and `systemctl --user list-unit-files --type=service`.

Run the dashboard as a user service under the account that owns any registered user units so that their systemd manager and journal are accessible. The service files in `deploy/` are templates. Review the account, paths, and permissions for each node.

Keep `Dashboard:EnableServiceControls` off for shared read-only deployments. Enable it only where every person with access is authorized to operate the configured units. The app never runs arbitrary command text; all operations target configured, validated systemd units.

### Network access

- The default listener is loopback-only. To serve a trusted LAN, configure an HTTPS Kestrel endpoint with a certificate trusted by the clients. Set `DashboardAccess:Enabled` to `true` and supply `DashboardAccess:Username` and `DashboardAccess:Password` through private deployment settings. Set `DashboardAccess:AllowedNetwork` to the LAN CIDR to restrict clients by source address. This single login grants access to the entire dashboard.
- Controls are disabled by default. Never run the dashboard as root to work around systemd permissions. Use narrowly scoped Polkit/sudo authorization if system units must be controlled.
- No secret/environment file import is implemented. Avoid logging credentials.
- Application URLs and mount references come from node configuration.

## Publish

```bash
dotnet publish src/GpuComputeDashboard/GpuComputeDashboard.csproj -c Release -o ./publish
```

Copy the publish directory to `/opt/gpu-dashboard`, then adjust `deploy/gpu-dashboard.service` and install it to `/etc/systemd/system/gpu-dashboard.service`. If the runtime is not installed, use `dotnet publish -r linux-x64 --self-contained true` and adjust ExecStart accordingly.

## Architecture and Codex continuation

See [docs/CODEX-HANDOFF.md](docs/CODEX-HANDOFF.md) for prioritized implementation tasks and constraints.
