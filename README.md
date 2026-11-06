# AutoPortal System Guardian

An open-source Windows tray application that **monitors hardware temperatures, fans and system load, diagnoses likely causes of overheating and freezes, and keeps a local record that survives a power cut**, so that after a freeze you can see what the PC was doing beforehand.

> Status: **0.1.0 (early release)**. See [Known limitations](#known-limitations) before relying on it.

## What it does
- Reads CPU / GPU / motherboard / disk temperatures, fan RPM, clocks and power through [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor). **Sensors your hardware doesn't expose show "Not available"; values are never invented.**
- Rule-based thermal diagnosis with configurable Elevated / High / Critical limits per component, plus spike, sustained-heat, idle-hot, throttling-indicator and fan checks.
- Recommendations are labeled as *possibilities* and list the recorded evidence behind them.
- Reads Windows Event Log sources: Kernel-Power 41, unexpected shutdowns (6008), WHEA, bugchecks, display-driver resets, disk errors, app crashes/hangs, TCP/IP port exhaustion, service timeouts.
- Saves samples to a local SQLite database every few seconds (WAL mode). If the previous session ended without a clean exit, the next start tells you when the last sample was recorded. It does **not** claim data from during the freeze itself.
- Watches for processes with abnormal handle counts and for socket exhaustion, common causes of slow degradation followed by hangs.
- Starts at logon through Task Scheduler (normal rights, optional delay, restart-on-failure) and runs quietly in the tray.
- Exports a Markdown diagnostic report. Nothing is uploaded. No telemetry.

## What it deliberately does *not* do
It never disables thermal protection, never turns fans off, never overclocks/undervolts, never writes to undocumented hardware registers, never reboots Windows, never kills processes on its own (critical Windows processes are marked "do not end"), and never resets a network adapter without explicit approval. Changing fan speed needs explicit confirmation and a "restore defaults" action exists.

## Install
1. Download `AutoPortal-System-Guardian-Setup-x.y.z.exe` (or the portable `.zip`) from [Releases](../../releases).
2. Verify the SHA-256 in `SHA256SUMS.txt`: `Get-FileHash .\AutoPortal-System-Guardian-Setup-0.1.0.exe`.
3. Run it. No Python, Git or .NET install is needed (self-contained).
4. For full CPU/motherboard sensors, use **Settings → Restart as administrator**. Without it, those sensors show "Not available".

**Uninstall:** Settings → Apps → AutoPortal System Guardian → Uninstall. The uninstaller removes the startup task. Your history in `%LOCALAPPDATA%\AutoPortal\SystemGuardian` is kept unless you delete it (Settings → Delete diagnostic history).

## Build from source
```powershell
dotnet test
dotnet run --project src/ui -- --demo     # simulated data, clearly bannered
./scripts/build.ps1 -Version 0.1.0        # self-contained single-file exe + zip + checksums
```

## Repository layout
`src/core` models/settings/SQLite · `src/hardware` sensor providers & fan control · `src/analysis` thermal, cooling, recommendations, reports · `src/windows` event log, Task Scheduler, process monitor, recovery · `src/ui` WPF app · `tests/` · `docs/` · `scripts/` · `installer/`

## Known limitations
- **Compatibility is the hard part.** Sensor and fan availability varies by board. Fan *control* works only where LibreHardwareMonitor supports the Super I/O chip; many OEM boards (e.g. HP/Dell prebuilts) expose none. Monitoring still works.
- **Only tested so far on one Windows 11 machine.** The matrix in the spec (Intel/AMD CPUs, NVIDIA/AMD/Intel GPUs, Windows 10) is *not yet tested*. Reports from other hardware are welcome.
- Memory use is ~180 MB (WPF + sensor library), heavier than ideal for a tray tool.
- No SMART health yet (drive temperature appears only where the driver exposes it).
- Liquid-cooler pump detection only appears where the cooler reports a sensor.
- No screenshots yet; no code-signing certificate, so Windows SmartScreen will warn on first run.
- The Connectivity Manager module (captive-portal login) is a separate project and isn't included; Guardian doesn't depend on it.

License: MIT. Third-party: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
