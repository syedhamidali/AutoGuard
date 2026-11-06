using System.Text.Json;

namespace AutoPortal.Connectivity.Core;

public sealed class ConnectivitySettings
{
    public bool AutoLoginEnabled { get; set; }
    /// <summary>Hostname of the login portal (e.g. the FortiGate). Credentials are ONLY ever sent to this host.</summary>
    public string PortalHost { get; set; } = "";
    public string Username { get; set; } = "";
    public string ProbeUrl { get; set; } = "http://www.msftconnecttest.com/connecttest.txt";
    public string ProbeExpected { get; set; } = "Microsoft Connect Test";
    public int CheckIntervalSeconds { get; set; } = 30;
    /// <summary>FortiGate portals commonly use self-signed certificates. Applies to the configured PortalHost only.</summary>
    public bool TrustPortalCertificate { get; set; }
    public bool AllowHttpPortal { get; set; }
    /// <summary>Stop after this many failed logins in a row, to avoid locking the account.</summary>
    public int MaxConsecutiveFailures { get; set; } = 3;
    public int PauseMinutesAfterFailures { get; set; } = 30;

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoPortal", "Connectivity");
    public static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static ConnectivitySettings Load(string? path = null)
    {
        try { var p = path ?? FilePath; if (File.Exists(p)) return JsonSerializer.Deserialize<ConnectivitySettings>(File.ReadAllText(p)) ?? new(); }
        catch { }
        return new();
    }

    public void Save(string? path = null)
    {
        var p = path ?? FilePath; Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, JsonSerializer.Serialize(this, Opts)); // contains no password
    }
}
