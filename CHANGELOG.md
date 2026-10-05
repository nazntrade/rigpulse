# Changelog

## 1.0.3 — 2026-10-05

- Append CPU package power, individual discrete GPU watts and their component sum to the end of the overlay.
- Add a persistent Settings checkbox to enable or disable the entire power block.
- Preserve independent polling and fixed numeric widths; missing or stale power is unavailable rather than zero.

## 1.0.2 — 2026-10-05

- Add the original green pulse icon to the tray, application EXE, installer and application shortcuts. Include vector source and reproducible multi-resolution icon generation.

## 1.0.1 — 2026-10-04

- Restore fan coloring using each fan's configured RPM reference.
- Add green, yellow, orange and light-red levels for utilization, temperatures and fan speed; unavailable readings remain gray.
- Add editable CPU/system references and individual sensor overrides. High fan speed means cooling activity, not a fan fault.
- Preserve fixed width, independent polling and CPU-stress scheduling.
## 1.0.0 — 2026-10-04

- First stable release of the validated 0.1.3 application: independent polling workers, CPU stress scheduling fix, multi-GPU identity and compact fixed-width metrics.
- The user confirmed that the complete overlay remains responsive during their full CPU stress test on the validated PC. Hardware support on other systems remains subject to the documented limitations.
- No polling or layout behavior changes from 0.1.3; existing installation and sign-in settings are retained.

## 0.1.3 — 2026-10-04

- Place RAM and VRAM values directly after their labels by left-aligning the values inside the existing fixed-width cells. Reserved digit space now follows the value, preserving stable panel width.

## 0.1.2 — 2026-10-04

- Fix monitoring starvation during CPU stress: the overlay and both polling workers explicitly select `AboveNormal` instead of inheriting Task Scheduler's `BelowNormal` priority. Normal launch, scheduled launch and restarted workers use the same policy.
- Add a fixed-width middle dot between each GPU's temperature and VRAM label.
- Add diagnostic capture times, worker priorities and an optional 2–120 second duration. WPF layout diagnostics now record UI timing.
- Add reproducible bounded CPU-contention regression checks for real CPU/RAM polling and the actual WPF window. Preserve sensor sources, fan control independence, fixed numeric cells and existing startup settings.

## 0.1.1 — 2026-10-04

- Add visible group dividers between CPU, RAM, GPUs and individual fan readings while retaining stable width.

## 0.1.0 — 2026-10-04

- Initial compiled Windows preview with independent CPU/RAM and hardware sensor workers, NVIDIA multi-GPU telemetry, fixed metric cells, a self-contained portable EXE and installer.
