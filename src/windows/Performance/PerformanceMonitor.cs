using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace AutoPortal.Guardian.Windows.Performance;

public sealed record SystemUsage(double? CpuPercent, double? RamPercent, double RamAvailableMb, double? DiskPercent, double? DiskResponseMs, TimeSpan Uptime);
public sealed record ProcessUsage(int Pid, string Name, double CpuPercent, double RamMb, double DiskMbPerSec, int Handles, bool IsProtected);

/// <summary>Lightweight resource sampling. Failures yield null ("Not available"), never made-up numbers.</summary>
public sealed class PerformanceMonitor : IDisposable
{
    // Processes that must never be offered for termination.
    public static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    { "System", "Idle", "smss", "csrss", "wininit", "services", "lsass", "winlogon", "svchost", "dwm", "explorer", "fontdrvhost", "Registry", "MsMpEng", "TrustedInstaller" };

    PerformanceCounter? _cpu, _disk, _diskLat;
    readonly Dictionary<int, (TimeSpan cpu, ulong io, DateTime t)> _prev = new();

    public PerformanceMonitor()
    {
        _cpu = TryCounter("Processor", "% Processor Time", "_Total");
        _disk = TryCounter("PhysicalDisk", "% Disk Time", "_Total");
        _diskLat = TryCounter("PhysicalDisk", "Avg. Disk sec/Transfer", "_Total");
        _cpu?.NextValue(); _disk?.NextValue(); _diskLat?.NextValue();
    }

    static PerformanceCounter? TryCounter(string cat, string name, string inst)
    {
        try { return new PerformanceCounter(cat, name, inst, true); } catch { return null; }
    }

    static double? Next(PerformanceCounter? c) { try { return c?.NextValue(); } catch { return null; } }

    [StructLayout(LayoutKind.Sequential)]
    struct MemStatus { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirt, AvailVirt, AvailExt; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStatus m);
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr h, out IoCounters c);

    public SystemUsage ReadSystem()
    {
        var m = new MemStatus { Length = (uint)Marshal.SizeOf<MemStatus>() };
        double? ram = null; double avail = 0;
        if (GlobalMemoryStatusEx(ref m)) { ram = m.Load; avail = m.AvailPhys / 1048576.0; }
        var lat = Next(_diskLat);
        return new SystemUsage(Next(_cpu), ram, avail, Next(_disk) is double d ? Math.Min(100, d) : null, lat is double l ? l * 1000 : null,
            TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    /// <summary>Per-process usage since the previous call.</summary>
    public IReadOnlyList<ProcessUsage> ReadProcesses()
    {
        var now = DateTime.UtcNow; var list = new List<ProcessUsage>();
        var seen = new HashSet<int>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    var cpuT = p.TotalProcessorTime; ulong io = 0;
                    if (GetProcessIoCounters(p.Handle, out var c)) io = c.ReadBytes + c.WriteBytes;
                    double cpu = 0, disk = 0;
                    if (_prev.TryGetValue(p.Id, out var pr))
                    {
                        var secs = (now - pr.t).TotalSeconds;
                        if (secs > 0)
                        {
                            cpu = (cpuT - pr.cpu).TotalSeconds / secs / Environment.ProcessorCount * 100;
                            disk = io >= pr.io ? (io - pr.io) / 1048576.0 / secs : 0;
                        }
                    }
                    _prev[p.Id] = (cpuT, io, now); seen.Add(p.Id);
                    list.Add(new ProcessUsage(p.Id, p.ProcessName, Math.Max(0, cpu), p.WorkingSet64 / 1048576.0, disk, p.HandleCount, Protected.Contains(p.ProcessName)));
                }
                catch { /* access denied for protected processes: skip */ }
            }
        }
        foreach (var k in _prev.Keys.Where(k => !seen.Contains(k)).ToList()) _prev.Remove(k);
        return list;
    }

    /// <summary>Counts UDP/TCP endpoints per process. A process owning thousands is a likely socket leak.</summary>
    public static int UdpEndpointCount() { try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Length; } catch { return -1; } }
    public static int TcpConnectionCount() { try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length; } catch { return -1; } }

    public void Dispose() { _cpu?.Dispose(); _disk?.Dispose(); _diskLat?.Dispose(); }
}
