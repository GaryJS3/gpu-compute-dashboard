# Codex handoff

## Goal
Improve and deploy the local-per-VM .NET 10 Blazor Interactive Server dashboard. The first reference VM is `ai4-v100` (Ubuntu 24.04.4, V100 32 GB, 24 vCPU, ~32 GB RAM). Prioritize reliable management over general orchestration.

## Existing code
- `Services/TelemetryMonitor.cs`: 5-second snapshot loop; NVIDIA CLI and limited legacy ROCm SMI, Linux metrics.
- `Services/WorkloadManager.cs`: explicitly registered systemd services; safe, fixed argument invocation; journal and mount checks.
- `Components/Pages/`: Blazor pages for the Command Center UI.
- `wwwroot/css/app.css`: reusable dark visual system and responsive layout.
- `appsettings.json`: initial 6 V100 workloads; **five system unit names are unverified placeholders**.
- `.github/workflows/build.yml`: CI build gate.

## Do first on the actual VM
1. Build/run and fix any SDK, Razor, or CSS issues.
2. Capture real `systemctl cat` output for the five system services and the user service. Update unit names, scopes and paths; preserve the existing units.
3. Check nvidia-smi response and metrics, journal access, and NFS mount detection under the `ai` account.
4. Test read-only controls first. Add authorization before enabling mutations.
5. Verify responsive behavior at desktop and mobile widths and clean up UI rough edges.

## Recommended next work
- Add explicit identity/authentication and per-operation authorization before exposing service controls.
- GPU process attribution by PID and cgroup, including child processes. Show 'unknown' when attribution is inconclusive.
- Per-application detail pages with logs/configuration and safe preset controls (ComfyUI, Llama Monitor, Unsloth).
- Add persisted telemetry history (SQLite), configurable retention and sampling.
- Systemd service discovery/import; preserve unit files and locally patched source environments.
- Support Docker/Compose as a separate opt-in provider, with Dockhand ownership respected.
- Improve AMD MI50 support using observed `rocm-smi` JSON from its installed ROCm version.

## Safety/design rules
Do not make shell commands or unit paths controllable from untrusted browser input. Never invoke `sh -c`. Do not auto-upgrade Python envs, CUDA/ROCm, or rewrite existing systemd units. No fake live data: demonstration data must be labeled. Keep UI per-node and independent from a central service.
