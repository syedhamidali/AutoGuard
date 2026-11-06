# Third-party notices

AutoPortal System Guardian is MIT licensed. It uses these third-party components.
Check each license before redistributing builds.

| Component | License | Notes |
|---|---|---|
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | MPL-2.0 | Sensor access. Used unmodified as a NuGet dependency. MPL-2.0 is file-level copyleft: if you modify the library's own files, those modifications must be published under MPL-2.0. Source: the link above. |
| HidSharp (LibreHardwareMonitorLib dependency) | Apache-2.0 | |
| Microsoft.Data.Sqlite / SQLitePCLRaw | MIT / Apache-2.0 | Local history database. |
| System.Diagnostics.PerformanceCounter, Microsoft.Win32.SystemEvents | MIT | Microsoft .NET libraries. |
| .NET 8 runtime | MIT | Bundled in self-contained builds. |

LibreHardwareMonitorLib loads a signed kernel driver (WinRing0-based) to read CPU and motherboard
sensors. That is why administrator rights are needed for full sensor access. See docs/SECURITY-MODEL.md.
