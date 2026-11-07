# Changelog

## 0.1.2
- Connectivity Manager: support portals on plain HTTP and non-standard ports (e.g. FortiGate at http://10.1.2.2:1000/fgtauth?<magic>) via an explicit opt-in switch; detect portals that redirect from the page body; never send credentials if the portal address can't be determined.

## 0.1.1
- **New module: AutoPortal Connectivity Manager** (independent of Guardian): captive-portal detection, automatic login to a FortiGate-style username/password portal, connection monitoring. Password is stored in Windows Credential Manager (never in files); credentials are only sent to the configured portal host; stops after repeated failures to protect the account.
- Guardian: drive health (Windows Storage health + SMART failure prediction where available), dashboard wording fix, locale-safe number parsing, screenshot.
- Build: packages both modules, builds both installers (release), verifies artifacts (`scripts/verify-artifacts.ps1`).
- Tests: 80 (added tests for scripts, installers, workflows and the portal login flow).

## 0.1.0
- Sensor monitoring via LibreHardwareMonitor; "Not available" for missing sensors.
- Thermal, cooling and recommendation rule engines with evidence; configurable limits.
- SQLite history with unclean-session (freeze / power-cut) detection; Markdown report export.
- Windows Event Log stability reader; handle-leak and socket-exhaustion watch.
- Task Scheduler startup, tray app, sleep/wake resume, crash-restart.
- WPF dashboard (7 pages), demo mode (`--demo`).


