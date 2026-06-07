using Microsoft.Data.Sqlite;

namespace ProgramClock.Data;

/// <summary>Tracks executables the user has blocked from being detected. Shares the single SQLite
/// connection with the other repositories and locks on it so writes from the UI thread don't collide
/// with the tracker's flush thread.</summary>
public sealed class BlocklistRepository
{
    private readonly SqliteConnection _conn;

    public BlocklistRepository(SqliteConnection conn) => _conn = conn;

    public List<string> List()
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT exe_name FROM blocked_apps ORDER BY exe_name COLLATE NOCASE;";
            var list = new List<string>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(r.GetString(0));
            return list;
        }
    }

    public void Block(string exeName)
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO blocked_apps(exe_name, blocked_at) VALUES($e,$t) " +
                "ON CONFLICT(exe_name) DO NOTHING;";
            cmd.Parameters.AddWithValue("$e", exeName);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }

    public void Unblock(string exeName)
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM blocked_apps WHERE exe_name=$e COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("$e", exeName);
            cmd.ExecuteNonQuery();
        }
    }

    // ── Blocked websites ──────────────────────────────────────────────────────────────────────────
    // Blocking a website is global by host (every browser stops accruing per-site time for it). The
    // browser's own run/focus totals keep tracking — only the per-site breakdown drops the host.

    public List<string> ListSites()
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT host FROM blocked_sites ORDER BY host COLLATE NOCASE;";
            var list = new List<string>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(r.GetString(0));
            return list;
        }
    }

    public void BlockSite(string host)
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO blocked_sites(host, blocked_at) VALUES($h,$t) " +
                "ON CONFLICT(host) DO NOTHING;";
            cmd.Parameters.AddWithValue("$h", host);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }

    public void UnblockSite(string host)
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM blocked_sites WHERE host=$h COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("$h", host);
            cmd.ExecuteNonQuery();
        }
    }
}
