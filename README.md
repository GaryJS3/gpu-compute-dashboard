# VectorGPU — GPU Compute Dashboard

Per-VM compute dashboard built with **ASP.NET Core 10 / Blazor Interactive Server**. Designed for Ubuntu GPU passthrough guests; first example targets `ai4-v100` (Tesla V100 32 GB) with six systemd workloads.

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

**Not implemented:** automatic service discovery/adoption, Docker management, GPU per-process VRAM attribution, persisted metrics, model management, configuration editing, centralized orchestration, built-in authentication or backup/migration automation. These are follow-on tasks.

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

Production settings enable real monitoring and **disable** service changes. The web server is bound to localhost by default. To view it from another computer:

```bash
ssh -L 5088:127.0.0.1:5088 ai@10.0.0.44
```

Then open **http://127.0.0.1:5088** on your workstation.

## Configure V100 services

Edit `src/GpuComputeDashboard/appsettings.json` or use an environment-specific override. **The five system service unit names are initial guesses** based on the inventory, not verified actual names. Only `llama-monitor.service` was supplied explicitly as a user unit.

Verify actual service names and correct them before using controls:

```bash
systemctl list-unit-files --type=service
systemctl --user list-unit-files --type=service
systemctl show comfyui.service --property=ActiveState,SubState,MainPID,MemoryCurrent
```

For Llama Monitor, prefer running the dashboard as a **user-level service** using \`deploy/gpu-dashboard.user.service\` (and \`loginctl enable-linger ai\`). This makes \`systemctl --user\` control available. The service account running the dashboard must have access to the correct **user systemd manager** for `systemctl --user` and `journalctl --user` to work. For the V100, installing/running the dashboard under user `ai` is the simplest option. The service file in `deploy/` is a starter template; review paths and sandbox restrictions on the actual machine.

Set `Dashboard:EnableServiceControls` to `true` **only after** adding authentication/authorization or restricting access to trusted administrators. The app never runs arbitrary command text; all operations target configured, validated systemd units.

### IMPORTANT security limitations

- No built-in authentication/authorization in this initial prototype. **Do not expose it directly to a LAN, reverse proxy, or the Internet.** Use SSH forwarding or implement access control first.
- Controls are disabled by default. Never run the dashboard as root to work around systemd permissions. Use narrowly scoped Polkit/sudo authorization if system units must be controlled.
- No secret/environment file import is implemented. Avoid logging credentials.
- Application URLs and mount references are initial V100 sample values, not real-time discovery.

## Publish

```bash
dotnet publish src/GpuComputeDashboard/GpuComputeDashboard.csproj -c Release -o ./publish
```

Copy the publish directory to `/opt/gpu-dashboard`, then adjust `deploy/gpu-dashboard.service` and install it to `/etc/systemd/system/gpu-dashboard.service`. If the runtime is not installed, use `dotnet publish -r linux-x64 --self-contained true` and adjust ExecStart accordingly.

## Architecture and Codex continuation

See [docs/CODEX-HANDOFF.md](docs/CODEX-HANDOFF.md) for prioritized implementation tasks and constraints.
