# Design and migration

The original PowerShell overlay polled LibreHardwareMonitor and formatted an automatically sized WPF text run. Under CPU stress, its shared UI/polling execution path could lag; changing digit counts also changed its width. RigPulse replaces that path with compiled C# and fixed metric cells.

The old overlay remains outside this public repository as a local rollback copy. Machine-specific settings, logs and diagnostic snapshots are not published.

## Process boundaries

`RigPulse.exe` hosts a WPF overlay and tray/settings UI. It launches owned child instances:

- `--system-worker`: CPU utilization and physical RAM through Windows APIs.
- `--sensor-worker`: LibreHardwareMonitor CPU temperature, motherboard and AMD/Intel sensors; NVIDIA NVML per-device telemetry.

Each child emits JSON lines on a redirected pipe. Events update snapshots; a 500 ms UI timer reads those snapshots. Reading hardware is never performed on the UI thread. The UI retains the previous hardware topology through an outage; stale values become unavailable rather than disappearing or remaining misleadingly current. Only owned worker processes can be restarted or terminated.

## Scope

Read-only monitoring. No fan control, overclocking, online account, telemetry service or project integration. User settings and optional sign-in registration are local and per-user. Elevation is an explicit tray command. Installing the optional official sensor driver also requires Windows administrator approval, while the RigPulse application itself installs per-user.

## Release policy

An unsigned build must never be labeled signed. A signed public release requires a trusted certificate/service and signature validation of the application and installer; timestamps allow valid signatures to survive certificate expiry. CI artifacts remain unsigned review builds until signing is configured. Binary artifacts belong in GitHub Releases, while source, build scripts, dependency locks and documentation belong in Git.
