using AutoPortal.Guardian.Analysis.Cooling;
using AutoPortal.Guardian.Analysis.Diagnostics;
using AutoPortal.Guardian.Analysis.Thermal;
using AutoPortal.Guardian.Core.Config;
using AutoPortal.Guardian.Core.Models;
using AutoPortal.Guardian.Core.Storage;
using AutoPortal.Guardian.Windows.Diagnostics;
using AutoPortal.Guardian.Windows.Performance;

namespace AutoPortal.Guardian.App.Services;

public sealed record EngineState(
    Snapshot Snapshot, SystemUsage Usage, IReadOnlyList<Finding> Findings, IReadOnlyList<Recommendation> Recommendations,
    IReadOnlyList<ProcessUsage> TopCpu, IReadOnlyList<ProcessUsage> TopRam, IReadOnlyList<ProcessUsage> TopDisk,
    IReadOnlyList<Sample> History, IReadOnlyList<StabilityEvent> Events, string Health, int UdpEndpoints, int TcpConnections);

/// <summary>
/// The monitoring loop: sample sensors, persist history (so it survives a power cut), run analyzers, raise alerts.
/// Designed to be light: one timer, in-memory ring buffer, process scan only every few ticks.
/// </summary>
public sealed class GuardianEngine : IDisposable
{
    const int RingSize = 1800;
    readonly GuardianSettings _s;
    readonly ISensorProvider _sensors;
    readonly HistoryStore _store;
    readonly PerformanceMonitor _perf = new();
    readonly ThermalAnalyzer _thermal;
    readonly CoolingAnalyzer _cooling;
    readonly RecommendationEngine _rec;
    readonly List<Sample> _ring = new();
    readonly Dictionary<string, DateTime> _lastAlert = new();
    readonly DateTime _sessionStart = DateTime.Now;
    Timer? _timer;
    int _busy, _tick;
    DateTime _lastPersist = DateTime.MinValue, _lastEvents = DateTime.MinValue, _lastPurge = DateTime.MinValue;
    IReadOnlyList<ProcessUsage> _procs = Array.Empty<ProcessUsage>();
    IReadOnlyList<StabilityEvent> _events = Array.Empty<StabilityEvent>();

    public (DateTime Start, DateTime? LastSample)? UncleanPreviousSession { get; }
    public EngineState? Last { get; private set; }
    public event Action<EngineState>? Updated;
    public event Action<Finding>? Alert;

    public GuardianEngine(GuardianSettings s, ISensorProvider sensors, HistoryStore store)
    {
        _s = s; _sensors = sensors; _store = store;
        _thermal = new ThermalAnalyzer(s); _cooling = new CoolingAnalyzer(s); _rec = new RecommendationEngine(s);
        UncleanPreviousSession = store.BeginSession(_sessionStart);
        if (UncleanPreviousSession is { } u)
            store.LogEvent("UncleanShutdown", $"Previous session (started {u.Start:g}) did not end cleanly. Last recorded sample: {u.LastSample?.ToString("g") ?? "none"}.");
    }

    public void Start() { _timer = new Timer(_ => Tick(), null, 0, Math.Max(1, _s.IntervalSeconds) * 1000); }
    public void Pause() => _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    public void Resume() { Tick(); _timer?.Change(Math.Max(1, _s.IntervalSeconds) * 1000, Math.Max(1, _s.IntervalSeconds) * 1000); }

    void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return; // never overlap
        try
        {
            var snap = _sensors.Read();
            var usage = _perf.ReadSystem();
            _tick++;
            if (_tick % 5 == 1) { _procs = _perf.ReadProcesses(); WatchLeaks(); }
            if (DateTime.Now - _lastEvents > TimeSpan.FromMinutes(5)) { _lastEvents = DateTime.Now; _events = StabilityReader.Read(TimeSpan.FromDays(7)); }

            var sample = new Sample(snap.Timestamp, snap.CpuTemp, snap.GpuTemp, snap.GpuHotspot, snap.MoboTemp,
                snap.CpuLoad ?? usage.CpuPercent, snap.GpuLoad, usage.RamPercent ?? snap.RamLoad, usage.DiskPercent,
                snap.CpuFanRpm, snap.GpuFanRpm);
            _ring.Add(sample); if (_ring.Count > RingSize) _ring.RemoveAt(0);

            // Persist often so the last minutes before a freeze/power cut are on disk.
            if (!snap.IsDemo && (DateTime.Now - _lastPersist).TotalSeconds >= _s.PersistEverySeconds) { _store.Add(sample); _lastPersist = DateTime.Now; }
            if (DateTime.Now - _lastPurge > TimeSpan.FromHours(6)) { _lastPurge = DateTime.Now; _store.Purge(_s.RetentionDays); }

            var findings = _thermal.Analyze(snap, _ring).Concat(_cooling.Analyze(snap, _ring)).ToList();
            var recs = _rec.Build(snap, findings, _events, UncleanPreviousSession != null);
            foreach (var f in findings.Where(f => f.Level >= ThermalLevel.High)) RaiseAlert(f);

            var health = findings.Any(f => f.Level == ThermalLevel.Critical) ? "Critical"
                       : findings.Any(f => f.Level == ThermalLevel.High) ? "Needs attention"
                       : findings.Any() || _events.Any(e => e.Time > DateTime.Now.AddHours(-24) && e.Category is "UnexpectedShutdown" or "WHEA" or "Disk") ? "Watch" : "Good";

            Last = new EngineState(snap, usage, findings, recs,
                _procs.OrderByDescending(p => p.CpuPercent).Take(8).ToList(),
                _procs.OrderByDescending(p => p.RamMb).Take(8).ToList(),
                _procs.OrderByDescending(p => p.DiskMbPerSec).Take(8).ToList(),
                _ring.ToList(), _events, health, PerformanceMonitor.UdpEndpointCount(), PerformanceMonitor.TcpConnectionCount());
            Updated?.Invoke(Last);
        }
        catch (Exception e) { try { _store.LogEvent("EngineError", e.Message); } catch { } }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    /// <summary>Records processes with abnormal handle counts (a common cause of slowly worsening hangs).</summary>
    void WatchLeaks()
    {
        foreach (var p in _procs.Where(p => p.Handles > 20000))
            if (Cooldown("leak:" + p.Name, 30))
                _store.LogEvent("HandleLeakSuspect", $"{p.Name} (PID {p.Pid}) holds {p.Handles:N0} handles, {p.RamMb:0} MB RAM");
        var udp = PerformanceMonitor.UdpEndpointCount();
        if (udp > 5000 && Cooldown("udp", 30)) _store.LogEvent("SocketExhaustionRisk", $"{udp:N0} UDP endpoints open");
    }

    bool Cooldown(string key, int minutes)
    {
        if (_lastAlert.TryGetValue(key, out var t) && DateTime.Now - t < TimeSpan.FromMinutes(minutes)) return false;
        _lastAlert[key] = DateTime.Now; return true;
    }

    void RaiseAlert(Finding f)
    {
        if (!_s.NotificationsEnabled || !Cooldown("alert:" + f.Code, _s.NotificationCooldownMinutes)) return;
        _store.LogEvent("Alert", f.Title + " - " + string.Join("; ", f.Evidence));
        Alert?.Invoke(f);
    }

    public string BuildReport(string? hardware = null)
    {
        var hist = _store.Range(DateTime.Now.AddDays(-1), DateTime.Now);
        var f = Last?.Findings ?? Array.Empty<Finding>();
        var r = Last?.Recommendations ?? Array.Empty<Recommendation>();
        return ReportBuilder.Build(DateTime.Now, hist, _events, f, r, UncleanPreviousSession, hardware);
    }

    public HistoryStore Store => _store;

    public void Dispose()
    {
        _timer?.Dispose();
        try { _store.EndSession(_sessionStart, DateTime.Now); } catch { }
        _perf.Dispose();
    }
}
