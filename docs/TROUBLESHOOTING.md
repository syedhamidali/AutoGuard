# Troubleshooting

**CPU temperature shows "Not available".** Running as administrator is not enough: the sensor library reads CPU/motherboard sensors through the separate **PawnIO** driver (https://pawnio.eu), which must be installed first. Some CPUs/boards still expose nothing; that is shown honestly. Drive temperatures only need administrator rights.

**How do I find out what caused a freeze?** Open `AutoPortal\BlackBox\guardian-blackbox.log` (on your non-system drive, e.g. `D:\`). Each line is one moment: `cpu`, `dpc`/`int`/`priv` (high values mean a driver or hardware, not a program), `disks=[… q=queue lat=latency]` (a growing queue or latency of seconds means a drive stalled) and `top=[…]` (the busiest programs). The last lines before the file stops are the last moments before the freeze.

**No fans listed / no fan control.** Your board's fan controller may not be supported by the sensor library (common on OEM prebuilt PCs). Use the BIOS/UEFI fan settings.

**SmartScreen warns on first run.** Builds are not code-signed yet. Check the SHA-256 against `SHA256SUMS.txt` from the release page.

**The app doesn't start at logon.** Open Task Scheduler and look for "AutoPortal System Guardian". Re-save Settings with "Start with Windows" ticked to recreate it.

**My PC froze and I power-cycled.** Reopen Guardian → System Health. It states when the last sample was recorded before the interruption and lists Windows events (Kernel-Power 41 with bugcheck code 0 means the PC lost power or was hard-reset, which is what a forced power-off causes; it's the *result* of the freeze, not the cause). Generate a report to see the minutes before the freeze.

**Where is crash information?** `%LOCALAPPDATA%\AutoPortal\SystemGuardian\crash.log`.
