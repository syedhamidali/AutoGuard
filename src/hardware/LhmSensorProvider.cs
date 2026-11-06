using System.Security.Principal;
using AutoPortal.Guardian.Core.Models;
using LibreHardwareMonitor.Hardware;

namespace AutoPortal.Guardian.Hardware;

/// <summary>
/// Reads real sensors through LibreHardwareMonitorLib (MPL-2.0). Only sensors that actually report a value are returned;
/// anything the hardware does not expose is simply absent and shown as "Not available" by the UI.
/// Low-level CPU/motherboard sensors need a kernel driver, which requires administrator rights.
/// </summary>
public sealed class LhmSensorProvider : ISensorProvider
{
    readonly Computer _pc;
    readonly object _lock = new();
    bool _disposed;

    public string Name => "LibreHardwareMonitor";
    public bool HasFullAccess { get; }

    public LhmSensorProvider()
    {
        HasFullAccess = IsAdmin();
        _pc = new Computer
        {
            IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true, IsMemoryEnabled = true,
            IsStorageEnabled = true, IsControllerEnabled = true, IsNetworkEnabled = false, IsPsuEnabled = false
        };
        _pc.Open();
    }

    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public Snapshot Read()
    {
        var list = new List<SensorReading>();
        lock (_lock)
        {
            if (_disposed) return new Snapshot { Readings = list };
            foreach (var hw in _pc.Hardware) Walk(hw, ComponentOf(hw.HardwareType, null), list);
        }
        return new Snapshot { Timestamp = DateTime.Now, Readings = list };
    }

    static void Walk(IHardware hw, ComponentKind comp, List<SensorReading> list)
    {
        hw.Update();
        foreach (var s in hw.Sensors)
        {
            var kind = Map(s.SensorType);
            if (kind is null || s.Value is null) continue;           // never invent: skip sensors without a value
            var c = comp;
            if (s.SensorType == SensorType.Fan) c = hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel ? ComponentKind.Gpu : ComponentKind.Fan;
            list.Add(new SensorReading(s.Identifier.ToString(), hw.Name, c, s.Name, kind.Value, s.Value));
        }
        foreach (var sub in hw.SubHardware) Walk(sub, comp == ComponentKind.Other ? ComponentOf(sub.HardwareType, comp) : comp, list);
    }

    static ComponentKind ComponentOf(HardwareType t, ComponentKind? fallback) => t switch
    {
        HardwareType.Cpu => ComponentKind.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => ComponentKind.Gpu,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController or HardwareType.Cooler => ComponentKind.Motherboard,
        HardwareType.Memory => ComponentKind.Memory,
        HardwareType.Storage => ComponentKind.Storage,
        _ => fallback ?? ComponentKind.Other
    };

    static SensorKind? Map(SensorType t) => t switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Load => SensorKind.Load,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Power => SensorKind.Power,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Fan => SensorKind.FanRpm,
        SensorType.Control => SensorKind.FanPercent,
        _ => null
    };

    /// <summary>Exposes controllable fan channels (read-only listing). Many boards expose none.</summary>
    public IReadOnlyList<FanChannel> FanChannels()
    {
        var l = new List<FanChannel>();
        lock (_lock)
            foreach (var hw in _pc.Hardware) Collect(hw, l);
        return l;
    }

    static void Collect(IHardware hw, List<FanChannel> l)
    {
        foreach (var s in hw.Sensors.Where(s => s.SensorType == SensorType.Control && s.Control != null))
            l.Add(new FanChannel(s.Identifier.ToString(), $"{hw.Name} / {s.Name}", s.Value, s.Control!.ControlMode.ToString()));
        foreach (var sub in hw.SubHardware) Collect(sub, l);
    }

    /// <summary>
    /// Sets a fan channel to a fixed percentage. REQUIRES explicit user confirmation (<paramref name="confirmed"/>),
    /// refuses values below the safety floor, and is only possible through the library's supported control interface.
    /// </summary>
    public (bool ok, string message) SetFan(string channelId, double percent, bool confirmed)
    {
        if (!confirmed) return (false, "Changing fan settings requires explicit confirmation.");
        if (percent < Analysis_MinPercent || percent > 100) return (false, $"Fan speed must be between {Analysis_MinPercent}% and 100%.");
        lock (_lock)
            foreach (var hw in _pc.Hardware)
                if (Find(hw, channelId) is { } s)
                {
                    try { s.Control!.SetSoftware((float)percent); return (true, $"Set to {percent:0}%."); }
                    catch (Exception e) { return (false, "This hardware rejected the change: " + e.Message); }
                }
        return (false, "Fan channel not found.");
    }

    /// <summary>Returns every controlled fan to the motherboard/firmware default behavior.</summary>
    public int RestoreDefaults()
    {
        int n = 0;
        lock (_lock)
            foreach (var hw in _pc.Hardware) n += Restore(hw);
        return n;
    }

    const double Analysis_MinPercent = 20;

    static ISensor? Find(IHardware hw, string id)
    {
        var s = hw.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Control && x.Control != null && x.Identifier.ToString() == id);
        if (s != null) return s;
        foreach (var sub in hw.SubHardware) { var r = Find(sub, id); if (r != null) return r; }
        return null;
    }

    static int Restore(IHardware hw)
    {
        int n = 0;
        foreach (var s in hw.Sensors.Where(s => s.SensorType == SensorType.Control && s.Control != null))
        { try { s.Control!.SetDefault(); n++; } catch { } }
        foreach (var sub in hw.SubHardware) n += Restore(sub);
        return n;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try { RestoreDefaultsUnlocked(); _pc.Close(); } catch { }
        }
    }

    void RestoreDefaultsUnlocked() { foreach (var hw in _pc.Hardware) Restore(hw); }
}

public sealed record FanChannel(string Id, string Name, double? CurrentPercent, string Mode);
