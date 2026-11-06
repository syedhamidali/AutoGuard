namespace AutoPortal.Guardian.Core.Models;

public enum ComponentKind { Cpu, Gpu, Motherboard, Memory, Storage, Fan, Other }
public enum SensorKind { Temperature, Load, Clock, Power, Voltage, FanRpm, FanPercent, Data }

/// <summary>One reading from a real hardware sensor. A null Value means "Not available" - values are never invented.</summary>
public sealed record SensorReading(
    string Id, string HardwareName, ComponentKind Component, string Name, SensorKind Kind, double? Value, bool IsDemo = false)
{
    public string Display(string unit) => Value is null ? "Not available" : $"{Value:0.#} {unit}";
}

/// <summary>A point-in-time view of all sensors plus accessors that return null when a sensor is missing.</summary>
public sealed class Snapshot
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public IReadOnlyList<SensorReading> Readings { get; init; } = Array.Empty<SensorReading>();
    public bool IsDemo => Readings.Any(r => r.IsDemo);

    IEnumerable<SensorReading> Of(ComponentKind c, SensorKind k) =>
        Readings.Where(r => r.Component == c && r.Kind == k && r.Value is not null);

    static double? NameMatch(IEnumerable<SensorReading> rs, params string[] names)
    {
        var list = rs.ToList();
        foreach (var n in names)
        {
            var m = list.FirstOrDefault(r => r.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
            if (m is not null) return m.Value;
        }
        return null;
    }

    static double? MaxOf(IEnumerable<SensorReading> rs) { var l = rs.Select(r => r.Value!.Value).ToList(); return l.Count == 0 ? null : l.Max(); }

    public double? CpuTemp => NameMatch(Of(ComponentKind.Cpu, SensorKind.Temperature), "Package", "Tctl", "Tdie", "CPU Total")
                              ?? MaxOf(Of(ComponentKind.Cpu, SensorKind.Temperature));
    public double? CpuLoad => NameMatch(Of(ComponentKind.Cpu, SensorKind.Load), "CPU Total");
    public double? CpuClock => MaxOf(Of(ComponentKind.Cpu, SensorKind.Clock).Where(r => r.Name.Contains("Core")));
    public double? CpuPower => NameMatch(Of(ComponentKind.Cpu, SensorKind.Power), "Package");
    public double? GpuTemp => NameMatch(Of(ComponentKind.Gpu, SensorKind.Temperature), "GPU Core", "Core");
    public double? GpuHotspot => NameMatch(Of(ComponentKind.Gpu, SensorKind.Temperature), "Hot Spot", "Hotspot");
    public double? GpuMemTemp => NameMatch(Of(ComponentKind.Gpu, SensorKind.Temperature), "Memory");
    public double? GpuLoad => NameMatch(Of(ComponentKind.Gpu, SensorKind.Load), "GPU Core", "Core");
    public double? GpuClock => NameMatch(Of(ComponentKind.Gpu, SensorKind.Clock), "GPU Core", "Core");
    public double? MoboTemp => NameMatch(Of(ComponentKind.Motherboard, SensorKind.Temperature), "System", "Motherboard", "Chipset");
    public double? RamLoad => NameMatch(Of(ComponentKind.Memory, SensorKind.Load), "Memory");
    public IReadOnlyList<SensorReading> Fans => Readings.Where(r => r.Kind == SensorKind.FanRpm).ToList();
    public double? CpuFanRpm => Fans.FirstOrDefault(f => f.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) && f.Component != ComponentKind.Gpu)?.Value;
    public double? GpuFanRpm => Fans.FirstOrDefault(f => f.Component == ComponentKind.Gpu)?.Value;
}

public interface ISensorProvider : IDisposable
{
    string Name { get; }
    /// <summary>True when the provider has the privileges/drivers needed to read low-level sensors (CPU temps, fans).</summary>
    bool HasFullAccess { get; }
    Snapshot Read();
}
