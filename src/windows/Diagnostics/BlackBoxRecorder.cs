using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using AutoPortal.Guardian.Windows.Performance;

namespace AutoPortal.Guardian.Windows.Diagnostics;

/// <summary>
/// A "flight recorder" for freezes. Every few seconds it appends ONE line describing what the machine was doing:
/// CPU, kernel/DPC/interrupt time, RAM, UDP endpoints, per-drive queue length and latency, SSD temperature and the busiest processes.
/// It is deliberately independent of the main app:
///  - it writes to a NON-system drive when one exists, so it keeps recording if the system drive (C:) stalls;
///  - the file is written by its own thread through a queue, so a stalled disk can never block sampling;
///  - each line is flushed to disk, so the final seconds before a power cut survive.
/// Local only: it records process names, never credentials or file contents, and the file is capped (rotated at a few MB).
/// </summary>
public sealed class BlackBoxRecorder : IDisposable
{
    public const string FileName = "guardian-blackbox.log";
    readonly string _path;
    readonly long _maxBytes;
    readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    readonly CancellationTokenSource _cts = new();
    readonly Task _writer;
    readonly PerformanceMonitor _perf = new();
    readonly List<(string Name, PerformanceCounter Queue, PerformanceCounter Latency)> _disks = new();
    Timer? _timer;
    int _busy;
    DateTime _lastTemp = DateTime.MinValue;
    string _temps = "n/a";

    public string Path => _path;

    public BlackBoxRecorder(string path, long maxBytes = 5 * 1024 * 1024)
    {
        _path = path; _maxBytes = maxBytes;
        try
        {
            foreach (var inst in new PerformanceCounterCategory("PhysicalDisk").GetInstanceNames().Where(i => i != "_Total"))
            {
                var q = new PerformanceCounter("PhysicalDisk", "Current Disk Queue Length", inst, true);
                var l = new PerformanceCounter("PhysicalDisk", "Avg. Disk sec/Transfer", inst, true);
                q.NextValue(); l.NextValue();
                _disks.Add((inst, q, l));
            }
        }
        catch { /* counters unavailable: the rest of the line is still recorded */ }
        _writer = Task.Run(WriteLoop);
    }

    /// <summary>Prefers a fixed non-system drive with free space, so recording survives a stalled system drive; falls back to the system drive.</summary>
    public static string PickPath()
    {
        try
        {
            var sys = System.IO.Path.GetPathRoot(Environment.SystemDirectory);
            var other = DriveInfo.GetDrives().FirstOrDefault(d => d.DriveType == DriveType.Fixed && d.IsReady && d.AvailableFreeSpace > 1L << 30
                && !string.Equals(d.RootDirectory.FullName, sys, StringComparison.OrdinalIgnoreCase));
            if (other != null) return System.IO.Path.Combine(other.RootDirectory.FullName, "AutoPortal", "BlackBox", FileName);
        }
        catch { }
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoPortal", "SystemGuardian", FileName);
    }

    public void Start(int intervalSeconds = 5) => _timer = new Timer(_ => Tick(), null, 0, Math.Max(2, intervalSeconds) * 1000);

    /// <summary>Queues a line for the writer thread. Never blocks.</summary>
    public void Write(string line) => _queue.Writer.TryWrite(line);

    void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var sys = _perf.ReadSystem();
            var k = _perf.ReadKernel();
            var procs = _perf.ReadProcesses();
            if (DateTime.Now - _lastTemp > TimeSpan.FromSeconds(30))
            {
                _lastTemp = DateTime.Now;
                var t = Task.Run(DiskHealth.ReadTemperatures);
                _temps = t.Wait(3000) ? FormatTemps(t.Result) : "timeout"; // a hung WMI call must not stall the recorder
            }
            Write(FormatLine(DateTime.Now, sys.CpuPercent, k, sys.RamPercent, PerformanceMonitor.UdpEndpointCount(), DiskSummary(), _temps, procs));
        }
        catch (Exception e) { Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} recorder-error {e.Message}"); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    string DiskSummary()
    {
        var parts = new List<string>();
        foreach (var (name, q, l) in _disks)
        {
            try { parts.Add(string.Create(CultureInfo.InvariantCulture, $"{name} q={q.NextValue():0} lat={l.NextValue() * 1000:0}ms")); } catch { parts.Add(name + " n/a"); }
        }
        return string.Join("; ", parts);
    }

    public static string FormatTemps(IReadOnlyList<DiskTemp> temps) =>
        temps.Count == 0 ? "n/a" : string.Join("; ", temps.Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.Name}={(t.TempC is double c ? c.ToString("0", CultureInfo.InvariantCulture) + "C" : "n/a")}{(t.MaxC is double m ? $"(max{m:0})" : "")}")));

    /// <summary>One log line. Pure function, covered by tests.</summary>
    public static string FormatLine(DateTime t, double? cpu, KernelBreakdown k, double? ram, int udp, string disks, string ssd, IReadOnlyList<ProcessUsage> procs)
    {
        static string N(double? v) => v is null ? "n/a" : v.Value.ToString("0", CultureInfo.InvariantCulture);
        var top = string.Join(" ", procs.OrderByDescending(p => p.CpuPercent).Take(3).Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name}:{p.CpuPercent:0}%")));
        return string.Create(CultureInfo.InvariantCulture,
            $"{t:yyyy-MM-dd HH:mm:ss} cpu={N(cpu)} dpc={N(k.Dpc)} int={N(k.Interrupt)} priv={N(k.Privileged)} ram={N(ram)}% udp={udp} disks=[{disks}] ssd=[{ssd}] top=[{top}]");
    }

    async Task WriteLoop()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            await foreach (var line in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    var fi = new FileInfo(_path);
                    if (fi.Exists && fi.Length > _maxBytes) File.Move(_path, _path + ".old", true);   // rotates only this recorder's own log
                    using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var sw = new StreamWriter(fs);
                    sw.WriteLine(line); sw.Flush(); fs.Flush(true);                                    // force to disk
                }
                catch { /* a failing disk must not crash the app */ }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    public void Dispose()
    {
        _timer?.Dispose(); _cts.Cancel(); _queue.Writer.TryComplete();
        try { _writer.Wait(1500); } catch { }
        _perf.Dispose();
        foreach (var d in _disks) { d.Queue.Dispose(); d.Latency.Dispose(); }
    }
}
