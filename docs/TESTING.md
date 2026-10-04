# Preview validation

Validated on 2026-10-04 using Windows x64, Intel Core i7-12700K, an MSI B760 DDR4 motherboard and two NVIDIA GeForce RTX 5060 Ti 16 GB cards. Reports containing machine-specific hardware identifiers are kept locally and are not committed.

| Check | Result |
| --- | --- |
| Release compile | Passed; zero warnings / errors. |
| Numeric layout / formatting tests | Passed: invariant formatting, invalid readings, future timestamps and unchanged fixed-cell widths. |
| Actual WPF width | Six alternating low/high reading samples stayed at 1080 device-independent pixels at 150% display scaling. The README image uses synthetic demo values. |
| GPU identity | Two separate NVIDIA UUIDs / driver indices detected; Intel graphics excluded by default. |
| NVIDIA VRAM | Allocated memory sampled through NVML v2 separately per card; the second card's zero allocation matched NVIDIA's tool instead of duplicating the first card's memory. Small sampling-time differences on an active desktop are expected. |
| CPU and motherboard sensors | CPU temperature, CPU fan RPM and both connected system fans read successfully when elevated with an existing official PawnIO installation. |
| Unprivileged run | CPU/RAM and NVIDIA readings worked; CPU temperature / motherboard sensors were unavailable until elevation. |
| Blocked hardware worker | Deliberately blocked sensor worker for an eight-second diagnostic run; eight distinct CPU/RAM frames continued to arrive. |
| Install / launch / uninstall | Silent per-user install into an isolated test directory passed. Installed EXE matched the published hash; its demo retained stable width. Silent uninstall removed the app and preserved the shared sensor driver. |

The included tests do not claim to validate every board, GPU vendor, monitor arrangement or Windows security configuration. A simultaneous full CPU stress test was not repeated after the earlier machine reached 100°C. AMD / Intel physical GPU telemetry, a clean machine's first PawnIO installation and trusted signing require separate validation.

## Reproduce

```powershell
dotnet run --project tests/RigPulse.Tests.csproj -c Release
./scripts/build.ps1
./artifacts/RigPulse-0.1.0-win-x64.exe --layout-test --output "$env:TEMP/rigpulse-layout.png"
./artifacts/RigPulse-0.1.0-win-x64.exe --diagnostics --simulate-sensor-hang --output "$env:TEMP/rigpulse-stall.json"
```

Layout testing writes a PNG and a JSON width report, then exits. Simulated stall diagnostics deliberately block only the sensor worker; CPU/RAM continues polling. Both tests clean up their owned workers on normal exit.

## 0.1.1 separator update

Visible dividers were added between CPU, RAM, each GPU and individual fan readings. The installed executable was checked on the same machine: six alternating low/high samples stayed at 1164 device-independent pixels; CPU temperature and both NVIDIA GPUs remained available. The updated synthetic screenshot is included in the README.

## 0.1.2 CPU scheduling regression

The installed scheduled task used priority 7. The live overlay and its two workers were running at Windows base priority 6 (`BelowNormal`), while FanControl ran at base priority 10 (`AboveNormal`).

A bounded integer workload saturated the same logical CPU used by the diagnostic monitor and its children. Comparing the same compiled executables on this machine:

| Check | 0.1.1 | 0.1.2 |
| --- | --- | --- |
| Busy CPU occupancy | 98.2% | 90.5% |
| Maximum gap between CPU/RAM frames | 6.07 seconds | 1.09 seconds |
| Distinct CPU/RAM frames in eight samples | 7 | 8 |
| Fresh CPU/RAM samples | Not measured by the old format | 8 / 8 |
| Main / system / sensor process priorities | Inherited `BelowNormal` | All `AboveNormal` |

This tests competition for a fully occupied logical CPU; it is not a claim of a repeated whole-machine OCCT/AVX thermal test. The earlier 100°C result is why that test is not run unattended. The regression does not change fan settings, clocks or power limits.

Reproduce after a release build on Windows with PowerShell 7:

```powershell
./tests/Test-CpuScheduling.ps1
# Optional comparison, if the old portable release was downloaded:
./tests/Test-CpuScheduling.ps1 -BaselinePath ./artifacts/RigPulse-0.1.1-win-x64.exe
```

The unit tests also force their own process to `BelowNormal`, apply the monitoring policy, verify `AboveNormal`, and restore the previous priority. Diagnostic reports now include sample capture time and each process's priority; the default remains eight seconds.

The actual WPF overlay was also tested under the same CPU contention: six UI capture samples, maximum interval 0.763 seconds for a 0.750-second timer, and constant width of 1188 device-independent pixels. The new middle dots between GPU temperature and VRAM occupy fixed space. An elevated physical-sensor check read CPU temperature, both distinct NVIDIA GPUs and all three active motherboard fans successfully.

To repeat the WPF test, close the installed overlay first, then run `./tests/Test-CpuScheduling.ps1 -Layout`. It displays synthetic values to exercise the window and writes a PNG and timing report; the default diagnostic test exercises actual CPU/RAM polling.
