using System.Text.RegularExpressions;

namespace AutoPortal.Connectivity.Core;

/// <summary>
/// Small local log so a failed portal login can be diagnosed afterwards. It records what the network sent and what the app decided,
/// and NEVER credentials. Values of form fields in captured pages are masked, and the file is capped at ~200 KB.
/// </summary>
public static class DiagnosticLog
{
    /// <summary>Tests point this at a temp file so they never touch the user's real log.</summary>
    public static string? PathOverride { get; set; }
    public static string DefaultPath => PathOverride ?? Path.Combine(ConnectivitySettings.Dir, "connectivity.log");
    const long MaxBytes = 200 * 1024;
    static readonly object Gate = new();

    public static void Write(string line, string? path = null)
    {
        try
        {
            var p = path ?? DefaultPath;
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                var fi = new FileInfo(p);
                if (fi.Exists && fi.Length > MaxBytes) File.Move(p, p + ".old", true);   // rotate this app's own log
                File.AppendAllText(p, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
            }
        }
        catch { /* logging must never break the app */ }
    }

    /// <summary>Collapses whitespace, masks field values, and truncates a captured page for the log.</summary>
    public static string Snippet(string? html, int max = 700)
    {
        if (string.IsNullOrEmpty(html)) return "(empty)";
        var s = Regex.Replace(html, @"(value\s*=\s*)(""[^""]*""|'[^']*')", "$1\"***\"", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > max ? s[..max] + "…" : s;
    }
}
