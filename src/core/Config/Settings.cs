using System.Text.Json;

namespace AutoPortal.Guardian.Core.Config;

public sealed record ThermalLimits(double Elevated, double High, double Critical);

public sealed class GuardianSettings
{
    public bool StartWithWindows { get; set; }
    public int StartupDelaySeconds { get; set; } = 30;
    public int IntervalSeconds { get; set; } = 2;
    public bool NotificationsEnabled { get; set; } = true;
    public int NotificationCooldownMinutes { get; set; } = 10;
    public int PersistEverySeconds { get; set; } = 5;
    public int RetentionDays { get; set; } = 14;
    public bool AutoRecovery { get; set; } = true;
    public bool AllowNetworkAdapterReset { get; set; }   // off: every reset needs explicit approval
    public bool Telemetry { get; set; }                  // never sends data; shown for transparency
    public ThermalLimits CpuLimits { get; set; } = new(70, 80, 90);
    public ThermalLimits GpuLimits { get; set; } = new(75, 83, 90);
    public ThermalLimits MoboLimits { get; set; } = new(55, 65, 75);
    public ThermalLimits DiskLimits { get; set; } = new(50, 55, 65);

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoPortal", "SystemGuardian");
    public static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static GuardianSettings Load(string? path = null)
    {
        try
        {
            var p = path ?? FilePath;
            if (File.Exists(p)) return JsonSerializer.Deserialize<GuardianSettings>(File.ReadAllText(p)) ?? new();
        }
        catch { /* corrupt settings fall back to defaults */ }
        return new();
    }

    public void Save(string? path = null)
    {
        var p = path ?? FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, JsonSerializer.Serialize(this, Opts));
    }
}
