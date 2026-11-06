using Microsoft.Data.Sqlite;

namespace AutoPortal.Guardian.Core.Storage;

public sealed record Sample(DateTime Time, double? CpuTemp, double? GpuTemp, double? GpuHotspot, double? MoboTemp,
    double? CpuLoad, double? GpuLoad, double? RamLoad, double? DiskLoad, double? CpuFan, double? GpuFan);

/// <summary>Local SQLite history. WAL + frequent commits so recent history survives a hard power cut.</summary>
public sealed class HistoryStore : IDisposable
{
    readonly SqliteConnection _db;
    readonly object _lock = new();

    public HistoryStore(string path)
    {
        if (path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
        Exec(@"CREATE TABLE IF NOT EXISTS samples(ts INTEGER PRIMARY KEY, cpu_t REAL, gpu_t REAL, gpu_hot REAL, mobo_t REAL,
                cpu_l REAL, gpu_l REAL, ram_l REAL, disk_l REAL, cpu_fan REAL, gpu_fan REAL);
              CREATE TABLE IF NOT EXISTS events(id INTEGER PRIMARY KEY AUTOINCREMENT, ts INTEGER, kind TEXT, message TEXT);
              CREATE TABLE IF NOT EXISTS sessions(start_ts INTEGER PRIMARY KEY, clean_end_ts INTEGER);");
    }

    void Exec(string sql)
    {
        lock (_lock) { using var c = _db.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
    }

    static object V(double? d) => d.HasValue ? d.Value : DBNull.Value;
    static double? D(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
    static long Ts(DateTime t) => new DateTimeOffset(t).ToUnixTimeSeconds();
    static DateTime FromTs(long t) => DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime;

    public void Add(Sample s)
    {
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "INSERT OR REPLACE INTO samples VALUES($ts,$p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9)";
            c.Parameters.AddWithValue("$ts", Ts(s.Time));
            var vals = new[] { s.CpuTemp, s.GpuTemp, s.GpuHotspot, s.MoboTemp, s.CpuLoad, s.GpuLoad, s.RamLoad, s.DiskLoad, s.CpuFan, s.GpuFan };
            for (int i = 0; i < vals.Length; i++) c.Parameters.AddWithValue("$p" + i, V(vals[i]));
            c.ExecuteNonQuery();
        }
    }

    public List<Sample> Range(DateTime from, DateTime to)
    {
        var list = new List<Sample>();
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "SELECT ts,cpu_t,gpu_t,gpu_hot,mobo_t,cpu_l,gpu_l,ram_l,disk_l,cpu_fan,gpu_fan FROM samples WHERE ts BETWEEN $f AND $t ORDER BY ts";
            c.Parameters.AddWithValue("$f", Ts(from));
            c.Parameters.AddWithValue("$t", Ts(to));
            using var r = c.ExecuteReader();
            while (r.Read())
                list.Add(new Sample(FromTs(r.GetInt64(0)), D(r, 1), D(r, 2), D(r, 3), D(r, 4), D(r, 5), D(r, 6), D(r, 7), D(r, 8), D(r, 9), D(r, 10)));
        }
        return list;
    }

    public void LogEvent(string kind, string message, DateTime? at = null)
    {
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "INSERT INTO events(ts,kind,message) VALUES($t,$k,$m)";
            c.Parameters.AddWithValue("$t", Ts(at ?? DateTime.Now));
            c.Parameters.AddWithValue("$k", kind);
            c.Parameters.AddWithValue("$m", message);
            c.ExecuteNonQuery();
        }
    }

    public List<(DateTime Time, string Kind, string Message)> Events(int max = 200)
    {
        var l = new List<(DateTime, string, string)>();
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "SELECT ts,kind,message FROM events ORDER BY ts DESC LIMIT $n";
            c.Parameters.AddWithValue("$n", max);
            using var r = c.ExecuteReader();
            while (r.Read()) l.Add((FromTs(r.GetInt64(0)), r.GetString(1), r.GetString(2)));
        }
        return l;
    }

    /// <summary>Starts a session. Returns the previous session if it never ended cleanly (crash, power cut or forced reset).</summary>
    public (DateTime Start, DateTime? LastSample)? BeginSession(DateTime now)
    {
        (DateTime, DateTime?)? unclean = null;
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "SELECT start_ts FROM sessions WHERE clean_end_ts IS NULL ORDER BY start_ts DESC LIMIT 1";
            if (c.ExecuteScalar() is long st)
            {
                using var c2 = _db.CreateCommand();
                c2.CommandText = "SELECT MAX(ts) FROM samples WHERE ts>=$s";
                c2.Parameters.AddWithValue("$s", st);
                var last = c2.ExecuteScalar();
                unclean = (FromTs(st), last is long l ? FromTs(l) : null);
                using var c3 = _db.CreateCommand();
                c3.CommandText = "UPDATE sessions SET clean_end_ts=-1 WHERE clean_end_ts IS NULL"; // -1 = ended uncleanly
                c3.ExecuteNonQuery();
            }
        }
        Exec($"INSERT OR REPLACE INTO sessions(start_ts,clean_end_ts) VALUES({Ts(now)},NULL)");
        return unclean;
    }

    public void EndSession(DateTime start, DateTime now) =>
        Exec($"UPDATE sessions SET clean_end_ts={Ts(now)} WHERE start_ts={Ts(start)}");

    /// <summary>Retention purge of data older than the configured period (user-configured setting).</summary>
    public void Purge(int retentionDays)
    {
        var cutoff = Ts(DateTime.Now.AddDays(-retentionDays));
        Exec($"DELETE FROM samples WHERE ts<{cutoff}; DELETE FROM events WHERE ts<{cutoff};");
    }

    /// <summary>User-initiated only: wipes all stored history.</summary>
    public void DeleteAll() => Exec("DELETE FROM samples; DELETE FROM events; DELETE FROM sessions;");

    public void Dispose() { lock (_lock) _db.Dispose(); }
}
