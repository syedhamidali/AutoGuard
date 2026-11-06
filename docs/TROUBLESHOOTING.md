# Troubleshooting

**CPU temperature shows "Not available".** Run Guardian as administrator (Settings → Restart as administrator). Some CPUs/boards still expose nothing; that is shown honestly.

**No fans listed / no fan control.** Your board's fan controller may not be supported by the sensor library (common on OEM prebuilt PCs). Use the BIOS/UEFI fan settings.

**SmartScreen warns on first run.** Builds are not code-signed yet. Check the SHA-256 against `SHA256SUMS.txt` from the release page.

**The app doesn't start at logon.** Open Task Scheduler and look for "AutoPortal System Guardian". Re-save Settings with "Start with Windows" ticked to recreate it.

**My PC froze and I power-cycled.** Reopen Guardian → System Health. It states when the last sample was recorded before the interruption and lists Windows events (Kernel-Power 41 with bugcheck code 0 means the PC lost power or was hard-reset, which is what a forced power-off causes; it's the *result* of the freeze, not the cause). Generate a report to see the minutes before the freeze.

**Where is crash information?** `%LOCALAPPDATA%\AutoPortal\SystemGuardian\crash.log`.
