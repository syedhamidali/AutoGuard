using AutoPortal.Guardian.Core.Models;

namespace AutoPortal.Guardian.Hardware;

/// <summary>
/// MOCK DATA for UI development and screenshots only (enabled with --demo). Every reading is flagged IsDemo
/// and the UI shows a permanent "DEMO DATA" banner. Never used for real diagnostics.
/// </summary>
public sealed class DemoSensorProvider : ISensorProvider
{
    readonly Random _r = new(42);
    double _t;
    public string Name => "DEMO DATA (not real hardware)";
    public bool HasFullAccess => true;

    public Snapshot Read()
    {
        _t += 0.1;
        double wave = (Math.Sin(_t) + 1) / 2;
        double cpuLoad = 15 + 60 * wave + _r.NextDouble() * 5;
        SensorReading R(string id, ComponentKind c, string n, SensorKind k, double v) => new("demo/" + id, "DEMO", c, n, k, v, true);
        return new Snapshot
        {
            Timestamp = DateTime.Now,
            Readings = new[]
            {
                R("c1", ComponentKind.Cpu, "CPU Package", SensorKind.Temperature, 38 + cpuLoad * 0.45),
                R("c2", ComponentKind.Cpu, "CPU Total", SensorKind.Load, cpuLoad),
                R("c3", ComponentKind.Cpu, "Core #1", SensorKind.Clock, 3200 + cpuLoad * 15),
                R("g1", ComponentKind.Gpu, "GPU Core", SensorKind.Temperature, 40 + wave * 25),
                R("g2", ComponentKind.Gpu, "GPU Hot Spot", SensorKind.Temperature, 48 + wave * 28),
                R("g3", ComponentKind.Gpu, "GPU Core", SensorKind.Load, wave * 80),
                R("m1", ComponentKind.Motherboard, "System", SensorKind.Temperature, 34 + wave * 6),
                R("r1", ComponentKind.Memory, "Memory", SensorKind.Load, 45 + wave * 10),
                R("f1", ComponentKind.Fan, "CPU Fan", SensorKind.FanRpm, 800 + cpuLoad * 12),
                R("f2", ComponentKind.Gpu, "GPU Fan", SensorKind.FanRpm, 600 + wave * 1200),
            }
        };
    }

    public void Dispose() { }
}
