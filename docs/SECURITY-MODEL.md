# Security & privilege model

- **Runs as the normal user** (`asInvoker`). The startup task uses `LeastPrivilege`; there is no UAC prompt at each logon.
- **Administrator is needed only for low-level sensors.** LibreHardwareMonitor loads a kernel driver to read CPU/motherboard sensors. The user opts in via *Settings → Restart as administrator*. Elevation is never automatic.
- **No network access** for data: there is no telemetry and no upload code. Reports are written only where the user chooses.
- **Local data:** `%LOCALAPPDATA%\AutoPortal\SystemGuardian` (settings.json, history.db). Retention is configurable; the user can delete everything.
- **Hardware writes:** only fan-speed through the library's supported control interface, with explicit confirmation, a 20% floor, and a restore-defaults call (also run on exit).
- **Never:** disables thermal protection or Windows security features, kills processes, reboots, or runs shell input built from untrusted text (adapter names are validated).
- Report vulnerabilities per [SECURITY.md](../SECURITY.md).
