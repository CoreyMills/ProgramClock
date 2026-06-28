using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ProgramClock.Data;

/// <summary>App rows and daily usage aggregation.</summary>
public sealed class UsageRepository
{
    private readonly SqliteConnection _conn;

    public UsageRepository(SqliteConnection conn) => _conn = conn;

    /// <summary>Insert the app if new (or refresh its metadata) and return its id.</summary>
    public long EnsureApp(string exeName, string? displayName, string? exePath, string? publisher)
    {
        lock (_conn)
        {
            using var sel = _conn.CreateCommand();
            sel.CommandText = "SELECT id FROM apps WHERE exe_name=$e;";
            sel.Parameters.AddWithValue("$e", exeName);
            var found = sel.ExecuteScalar();
            if (found is not null)
            {
                var id = Convert.ToInt64(found);
                if (displayName is not null || exePath is not null || publisher is not null)
                {
                    using var upd = _conn.CreateCommand();
                    upd.CommandText =
                        "UPDATE apps SET " +
                        "display_name = COALESCE(NULLIF($d,''), display_name), " +
                        "exe_path     = COALESCE(NULLIF($p,''), exe_path), " +
                        "publisher    = COALESCE(NULLIF($c,''), publisher) " +
                        "WHERE id=$id;";
                    upd.Parameters.AddWithValue("$d", (object?)displayName ?? "");
                    upd.Parameters.AddWithValue("$p", (object?)exePath ?? "");
                    upd.Parameters.AddWithValue("$c", (object?)publisher ?? "");
                    upd.Parameters.AddWithValue("$id", id);
                    upd.ExecuteNonQuery();
                }
                return id;
            }

            long? categoryId = EnsureAutoCategoryId(exeName, publisher);

            using var ins = _conn.CreateCommand();
            ins.CommandText =
                "INSERT INTO apps(exe_name,display_name,exe_path,publisher,category_id,first_seen) " +
                "VALUES($e,$d,$p,$c,$cat,$t); SELECT last_insert_rowid();";
            ins.Parameters.AddWithValue("$e", exeName);
            ins.Parameters.AddWithValue("$d", (object?)displayName ?? DBNull.Value);
            ins.Parameters.AddWithValue("$p", (object?)exePath ?? DBNull.Value);
            ins.Parameters.AddWithValue("$c", (object?)publisher ?? DBNull.Value);
            ins.Parameters.AddWithValue("$cat", (object?)categoryId ?? DBNull.Value);
            ins.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            return Convert.ToInt64(ins.ExecuteScalar());
        }
    }

    /// <summary>Resolve the auto-default category for a brand-new app, creating the (is_auto) category
    /// row on demand. Returns null when no rule matches, leaving the app uncategorized.</summary>
    private long? EnsureAutoCategoryId(string exeName, string? publisher)
    {
        var name = CategoryRules.Guess(exeName, publisher);
        if (name is null) return null;

        using var sel = _conn.CreateCommand();
        sel.CommandText = "SELECT id FROM categories WHERE name=$n COLLATE NOCASE LIMIT 1;";
        sel.Parameters.AddWithValue("$n", name);
        if (sel.ExecuteScalar() is { } found) return Convert.ToInt64(found);

        using var ins = _conn.CreateCommand();
        ins.CommandText =
            "INSERT INTO categories(name, is_auto, created_at) VALUES($n,1,$t); " +
            "SELECT last_insert_rowid();";
        ins.Parameters.AddWithValue("$n", name);
        ins.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        return Convert.ToInt64(ins.ExecuteScalar());
    }

    /// <summary>Add run/focus deltas (ms) to the given app's row for the given local date.</summary>
    public void AddUsage(long appId, string localDate, long runMs, long focusMs)
    {
        if (runMs == 0 && focusMs == 0) return;
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO usage_daily(app_id,date,run_ms,focus_ms) VALUES($a,$d,$r,$f) " +
                "ON CONFLICT(app_id,date) DO UPDATE SET " +
                "run_ms = run_ms + excluded.run_ms, focus_ms = focus_ms + excluded.focus_ms;";
            cmd.Parameters.AddWithValue("$a", appId);
            cmd.Parameters.AddWithValue("$d", localDate);
            cmd.Parameters.AddWithValue("$r", runMs);
            cmd.Parameters.AddWithValue("$f", focusMs);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Add focused-time delta (ms) for a website under a browser app, for the given date.</summary>
    public void AddSiteUsage(long appId, string host, string localDate, long focusMs)
    {
        if (focusMs == 0 || string.IsNullOrEmpty(host)) return;
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO site_usage_daily(app_id,host,date,focus_ms) VALUES($a,$h,$d,$f) " +
                "ON CONFLICT(app_id,host,date) DO UPDATE SET focus_ms = focus_ms + excluded.focus_ms;";
            cmd.Parameters.AddWithValue("$a", appId);
            cmd.Parameters.AddWithValue("$h", host);
            cmd.Parameters.AddWithValue("$d", localDate);
            cmd.Parameters.AddWithValue("$f", focusMs);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Per-website focused time over the range, keyed by the owning browser's exe name.</summary>
    public List<SiteUsageRow> QuerySites(DateRange range)
    {
        var (from, to) = RangeBounds(range);
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "SELECT a.exe_name, s.host, SUM(s.focus_ms) AS focus " +
                "FROM site_usage_daily s JOIN apps a ON a.id = s.app_id " +
                "WHERE ($from IS NULL OR s.date >= $from) AND ($to IS NULL OR s.date <= $to) " +
                "GROUP BY a.id, s.host HAVING focus > 0 " +
                "ORDER BY focus DESC;";
            cmd.Parameters.AddWithValue("$from", (object?)from ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$to", (object?)to ?? DBNull.Value);

            var rows = new List<SiteUsageRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add(new SiteUsageRow
                {
                    ExeName = r.GetString(0),
                    Host = r.GetString(1),
                    FocusMs = r.IsDBNull(2) ? 0 : r.GetInt64(2),
                });
            return rows;
        }
    }

    /// <summary>Remove an app and all its recorded usage (including its per-site rows). The app
    /// reappears the next time it is detected (EnsureApp recreates it), unless it has been blocked.</summary>
    public void DeleteApp(string exeName)
    {
        lock (_conn)
        {
            using var sel = _conn.CreateCommand();
            sel.CommandText = "SELECT id FROM apps WHERE exe_name=$e;";
            sel.Parameters.AddWithValue("$e", exeName);
            if (sel.ExecuteScalar() is not { } found) return;
            var id = Convert.ToInt64(found);

            using var delSites = _conn.CreateCommand();
            delSites.CommandText = "DELETE FROM site_usage_daily WHERE app_id=$id;";
            delSites.Parameters.AddWithValue("$id", id);
            delSites.ExecuteNonQuery();

            using var delUsage = _conn.CreateCommand();
            delUsage.CommandText = "DELETE FROM usage_daily WHERE app_id=$id;";
            delUsage.Parameters.AddWithValue("$id", id);
            delUsage.ExecuteNonQuery();

            using var delApp = _conn.CreateCommand();
            delApp.CommandText = "DELETE FROM apps WHERE id=$id;";
            delApp.Parameters.AddWithValue("$id", id);
            delApp.ExecuteNonQuery();
        }
    }

    /// <summary>Remove one website's recorded focused time under a single browser. Each focused tick is
    /// accrued to both the browser and the active site, so the browser's daily focus total includes the
    /// site's time; subtract it (per date, floored at 0) before dropping the site rows so the browser's
    /// displayed focused time reflects the removal. Run time is untouched — sites accrue no run time. The
    /// host reappears the next time it is visited in that browser (unless it has been blocked).</summary>
    public void DeleteSite(string exeName, string host)
    {
        lock (_conn)
        {
            using var sub = _conn.CreateCommand();
            sub.CommandText =
                "UPDATE usage_daily SET focus_ms = MAX(0, focus_ms - COALESCE((" +
                "  SELECT s.focus_ms FROM site_usage_daily s " +
                "  WHERE s.app_id = usage_daily.app_id AND s.date = usage_daily.date " +
                "    AND s.host = $h COLLATE NOCASE), 0)) " +
                "WHERE app_id IN (SELECT id FROM apps WHERE exe_name = $e) " +
                "  AND EXISTS (SELECT 1 FROM site_usage_daily s2 " +
                "    WHERE s2.app_id = usage_daily.app_id AND s2.date = usage_daily.date " +
                "      AND s2.host = $h COLLATE NOCASE);";
            sub.Parameters.AddWithValue("$h", host);
            sub.Parameters.AddWithValue("$e", exeName);
            sub.ExecuteNonQuery();

            using var del = _conn.CreateCommand();
            del.CommandText =
                "DELETE FROM site_usage_daily WHERE host=$h COLLATE NOCASE AND app_id IN " +
                "(SELECT id FROM apps WHERE exe_name=$e);";
            del.Parameters.AddWithValue("$h", host);
            del.Parameters.AddWithValue("$e", exeName);
            del.ExecuteNonQuery();
        }
    }

    /// <summary>Remove a website's recorded focused time across every browser (used when blocking a
    /// host, which is global). As in <see cref="DeleteSite"/>, subtract each browser's recorded focus
    /// for the host (per date, floored at 0) from its daily total before dropping the site rows.</summary>
    public void DeleteSiteByHost(string host)
    {
        lock (_conn)
        {
            using var sub = _conn.CreateCommand();
            sub.CommandText =
                "UPDATE usage_daily SET focus_ms = MAX(0, focus_ms - COALESCE((" +
                "  SELECT s.focus_ms FROM site_usage_daily s " +
                "  WHERE s.app_id = usage_daily.app_id AND s.date = usage_daily.date " +
                "    AND s.host = $h COLLATE NOCASE), 0)) " +
                "WHERE EXISTS (SELECT 1 FROM site_usage_daily s2 " +
                "    WHERE s2.app_id = usage_daily.app_id AND s2.date = usage_daily.date " +
                "      AND s2.host = $h COLLATE NOCASE);";
            sub.Parameters.AddWithValue("$h", host);
            sub.ExecuteNonQuery();

            using var del = _conn.CreateCommand();
            del.CommandText = "DELETE FROM site_usage_daily WHERE host=$h COLLATE NOCASE;";
            del.Parameters.AddWithValue("$h", host);
            del.ExecuteNonQuery();
        }
    }

    public List<UsageRow> Query(DateRange range)
    {
        var (from, to) = RangeBounds(range);
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            // Running time overlaps in wall-clock, so it's never summed: an app's running figure for
            // the range is its longest single day (MAX), while focused time accumulates (SUM).
            cmd.CommandText =
                "SELECT a.exe_name, COALESCE(a.display_name, a.exe_name) AS name, " +
                "MAX(u.run_ms) AS run, SUM(u.focus_ms) AS focus, " +
                "COALESCE(c.name, 'Uncategorized') AS category, " +
                "COALESCE(a.tag, 'Other') AS tag " +
                "FROM usage_daily u JOIN apps a ON a.id = u.app_id " +
                "LEFT JOIN categories c ON c.id = a.category_id " +
                "WHERE ($from IS NULL OR u.date >= $from) AND ($to IS NULL OR u.date <= $to) " +
                "GROUP BY a.id HAVING run > 0 OR focus > 0 " +
                "ORDER BY focus DESC, run DESC;";
            cmd.Parameters.AddWithValue("$from", (object?)from ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$to", (object?)to ?? DBNull.Value);

            var rows = new List<UsageRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new UsageRow
                {
                    ExeName = r.GetString(0),
                    DisplayName = r.GetString(1),
                    RunMs = r.IsDBNull(2) ? 0 : r.GetInt64(2),
                    FocusMs = r.IsDBNull(3) ? 0 : r.GetInt64(3),
                    CategoryName = r.GetString(4),
                    Tag = r.GetString(5).ParseTag(),
                });
            }
            return rows;
        }
    }

    /// <summary>Per-day totals (run/focused ms summed across all apps) over the range, oldest first.
    /// Feeds the daily-trend visualizer.</summary>
    public List<DailyRow> QueryDaily(DateRange range)
    {
        var (from, to) = RangeBounds(range);
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            // Per day: running takes the longest single app (MAX, never summed across apps because run
            // time overlaps in wall-clock); focused time sums.
            cmd.CommandText =
                "SELECT u.date, MAX(u.run_ms) AS run, SUM(u.focus_ms) AS focus " +
                "FROM usage_daily u " +
                "WHERE ($from IS NULL OR u.date >= $from) AND ($to IS NULL OR u.date <= $to) " +
                "GROUP BY u.date ORDER BY u.date;";
            cmd.Parameters.AddWithValue("$from", (object?)from ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$to", (object?)to ?? DBNull.Value);

            var rows = new List<DailyRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add(new DailyRow
                {
                    Date = r.GetString(0),
                    RunMs = r.IsDBNull(1) ? 0 : r.GetInt64(1),
                    FocusMs = r.IsDBNull(2) ? 0 : r.GetInt64(2),
                });
            return rows;
        }
    }

    public static string LocalDate(DateTime localNow) =>
        localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static (string? from, string? to) RangeBounds(DateRange range)
    {
        var today = DateTime.Now.Date;
        return range switch
        {
            DateRange.Today => (LocalDate(today), LocalDate(today)),
            DateRange.Week => (LocalDate(StartOfWeek(today)), LocalDate(today)),
            DateRange.Month => (LocalDate(new DateTime(today.Year, today.Month, 1)), LocalDate(today)),
            DateRange.All => (null, null),
            _ => (null, null),
        };
    }

    private static DateTime StartOfWeek(DateTime day)
    {
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        int diff = (7 + (day.DayOfWeek - firstDay)) % 7;
        return day.AddDays(-diff);
    }
}
