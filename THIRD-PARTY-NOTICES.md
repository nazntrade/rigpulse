# Third-party notices

RigPulse source is MIT-licensed. The components below retain their own licenses. Their source code and drivers are not modified by RigPulse. Full license texts are in `licenses/`, embedded in the portable application (tray menu: **Licenses**) and included with the installed application.

| Component | Version | License / corresponding source |
| --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0; [source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6). Copyright LibreHardwareMonitor and Contributors; portions copyright Michael Möller and OpenHardwareMonitor contributors. |
| BlackSharp.Core | 1.0.7 | MPL-2.0; [source](https://github.com/Blacktempel/BlackSharp). Copyright Florian K. |
| DiskInfoToolkit | 1.1.2 | MPL-2.0; [source](https://github.com/Blacktempel/DiskInfoToolkit). Copyright Florian K. |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0; [source](https://github.com/Blacktempel/RAMSPDToolkit). Copyright Florian K. |
| HidSharp | 2.6.4 | See `licenses/HidSharp-LICENSE.txt`; [source](https://github.com/IntergatedCircuits/HidSharp). Copyright 2010-2025 James Bellinger. |
| Mono.Posix.NETStandard | 1.0.0 | MIT and applicable notices in `licenses/Mono-LICENSE.txt`; [source](https://github.com/mono/mono). |
| System.IO.FileSystem.AccessControl | 5.0.0 | MIT; Microsoft .NET runtime notices. |
| System.IO.Ports | 10.0.3 | MIT; Microsoft .NET runtime notices. |
| System.Management | 10.0.2 | MIT; Microsoft .NET runtime notices. |
| Microsoft .NET / Windows Desktop runtime | 10.0.11 | MIT and applicable third-party notices in `licenses/dotnet-*`; [runtime source](https://github.com/dotnet/runtime), [desktop source](https://github.com/dotnet/wpf). |
| PawnIO modules bundled by LibreHardwareMonitor | 0.1.6 | LGPL-2.1; [source](https://github.com/namazso/PawnIO.Modules/tree/0.1.6), `licenses/PawnIO-modules-LICENSE.txt`. |
| Optional official PawnIO driver installer | 2.1.0 | GPL-2.0-or-later with upstream exception; [driver source](https://github.com/namazso/PawnIO/tree/2.1.0), [PawnPP dependency](https://github.com/namazso/PawnPP/tree/e64e4c37b2d8ba0d8ee57205faf8183aee12c438), [installer release](https://github.com/namazso/PawnIO.Setup/releases/tag/2.1.0). Full GPL and exception in `licenses/PawnIO-*`. Copyright 2026 namazso. |
| Inno Setup | Build tool | [License and source](https://github.com/jrsoftware/issrc). The compiler is not distributed with RigPulse source. |

The locked NuGet inventory, including build-only packages and non-Windows assets not distributed in the Windows executable, is in `src/RigPulse/packages.lock.json`. A corresponding-source archive for the bundled low-level driver and modules accompanies downloadable release binaries.

NVIDIA telemetry uses the NVML library supplied by the user's installed NVIDIA driver. That library is not redistributed by RigPulse. RigPulse calls read-only telemetry functions.

PawnIO is an optional system component shared with other programs. RigPulse's uninstaller does not remove it. Only the official signed driver installer is bundled. RigPulse does not select an unrestricted driver, modify modules or change Windows security policy.