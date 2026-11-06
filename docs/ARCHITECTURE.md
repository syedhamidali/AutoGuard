# Architecture

```
            +-------------------+
            |   WPF UI (src/ui) |  tray, dashboard, settings
            +---------+---------+
                      | EngineState
            +---------v---------+      +-------------------+
            |  GuardianEngine   +----->| HistoryStore      |  SQLite, WAL, session markers
            |  (one timer)      |      | (src/core)        |
            +--+------+------+--+      +-------------------+
               |      |      |
  ISensorProvider  PerformanceMonitor  StabilityReader
  (src/hardware)   (src/windows)       (src/windows, Event Log)
               |
        Analyzers (src/analysis, pure & unit-tested):
        ThermalAnalyzer, CoolingAnalyzer -> Findings -> RecommendationEngine -> ReportBuilder
```

**Dependency rule:** `core` and `analysis` have no Windows dependencies, so all diagnostic logic is testable without hardware. `hardware` is the only project that references LibreHardwareMonitor. The engine depends on the `ISensorProvider` interface, so a real, empty or clearly-labeled demo provider can be swapped in.

**Truthfulness rules (enforced by tests):** a sensor with no value is omitted, accessors return `null`, UI prints "Not available"; recommendations carry `Evidence` and an `IsPossibility` flag; the report states that nothing is known after the last recorded sample.

**Startup & recovery:** a per-user Task Scheduler task (logon trigger + delay, `InteractiveToken`, `LeastPrivilege`, restart on failure ×5). The app exits non-zero on unhandled exceptions so the scheduler relaunches it. `SystemEvents.PowerModeChanged` resumes monitoring after sleep. A session row without a clean end marker reveals a freeze/power cut on next start.

**Modules:** Guardian is independent of the AutoPortal Connectivity Manager. Both could share only `GuardianSettings`-style JSON in `%LOCALAPPDATA%\AutoPortal`; neither references the other.

## Things that need motherboard-specific support
Fan control, VRM/chipset temperatures and some voltages depend on the Super I/O / EC chip being supported by LibreHardwareMonitor. Where unsupported we only monitor and recommend BIOS/UEFI fan settings.
