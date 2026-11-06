# VectorGPU — GPU Compute Dashboard

Per-VM compute dashboard built with **ASP.NET Core 10 / Blazor Interactive Server**. Designed for Ubuntu GPU passthrough guests. The `ai4-v100` branch carries the inventory and deployment settings for the first VM.

The UI takes its direction from the **Command Center 01** HTML concept: dark sidebar, GPU KPI cards, charts, workload table, application cards, journal, mounts, and settings.

## Current capabilities

- Live **Linux host** sampling: CPU from `/proc/stat`, available RAM from `/proc/meminfo`, root filesystem and uptime.
- Live **NVIDIA GPU** sampling through `nvidia-smi` (utilization, VRAM, temperature, power, clocks, driver).
- Experimental **legacy ROCm SMI** detection for AMD nodes, with unavailable metrics clearly marked.
- NVIDIA compute VRAM by process, with workload attribution through systemd cgroups on cgroup v2 hosts.
- Rolling telemetry chart (in-memory, not persisted).
- Inspect explicitly registered **systemd system and user services**: state, main PID, unit memory.
- Start/stop/restart configured units, with confirmation, **disabled on real machines by default**.
- Per-unit journal output, registered NFS mount validation, application launch links.
- Development demonstration mode with sample V100 values and simulated controls.

**Not implemented:** automatic service discovery/adoption, Docker management, persisted metrics, model management, configuration editing, centralized orchestration, per-user accounts or backup/migration automation. These are follow-on tasks.

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

Set an application's `SupportingWorkloadIds` to a list of registered workload IDs to show their status and recent journal entries on its details page. Each supporting service keeps its own journal and Workloads entry.

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

## Deploy on ai4-v100

Run as `ai` from this checkout on the VM. This host already has systemd user lingering enabled. The setup script provisions a TLS certificate and generates a dashboard password on first run.

```bash
dotnet publish src/GpuComputeDashboard/GpuComputeDashboard.csproj -c Release -o "$HOME/.local/share/gpu-dashboard"
install -Dm644 deploy/gpu-dashboard.user.service "$HOME/.config/systemd/user/gpu-dashboard.service"
bash deploy/ai4-v100/setup-lan.sh
sudo install -m 644 deploy/ai4-v100/49-ai4-v100-dashboard.rules /etc/polkit-1/rules.d/49-ai4-v100-dashboard.rules
systemctl --user daemon-reload
systemctl --user enable gpu-dashboard.service
systemctl --user restart gpu-dashboard.service
```

Open **https://10.0.0.44:5088** from the `10.0.0.0/24` LAN. Trust the VM's self-signed certificate (`~/.config/gpu-dashboard/tls.crt`) on the workstation. Anyone on the configured subnet can access the dashboard and operate its registered workloads. To restore the login, set `DashboardAccess:Enabled` to `true`, set `DashboardAccess:AllowUnauthenticatedLan` to `false`, and redeploy. The provisioned password remains in `~/.config/gpu-dashboard/lan.env`. The Polkit rule allows only those operations on the five registered system units for `ai`. Llama Monitor runs in the same user systemd manager as the dashboard.
