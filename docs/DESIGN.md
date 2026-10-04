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

## CPU contention (0.1.2)

Task Scheduler defaults to priority 7, which maps to `BelowNormal` process priority; its children inherit that class. On the affected installation the overlay and both workers had base priority 6, while FanControl had base priority 10. Separating the processes did not fix scheduler starvation: under CPU contention all three RigPulse processes could stop updating together.

RigPulse now selects `AboveNormal` at the beginning of every entry point, and the parent also applies that class immediately after creating each worker. This covers normal launch, scheduled launch, elevation, diagnostics and watchdog replacement. The short periodic monitor work can preempt normal-priority CPU load. No affinity, power, fan or other application's priority is changed; `High` and `Realtime` are not used. Windows may still delay the whole desktop if a test uses higher priority or the machine itself stops responding.

The regression test pins the monitor and inherited worker affinities to one logical CPU and saturates that CPU with a bounded normal-priority integer workload. This reproduces the starvation path without the heat of a whole-machine AVX stress test. Raw readings and device identifiers remain local. The test restores its own priority/affinity and terminates only its own children.

Windows references: [scheduler priorities and inheritance](https://learn.microsoft.com/en-us/windows/win32/procthread/scheduling-priorities), [Task Scheduler priority mapping](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-priority).
