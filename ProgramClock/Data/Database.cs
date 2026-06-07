using System.IO;
using Microsoft.Data.Sqlite;

namespace ProgramClock.Data;

/// <summary>
/// Owns the SQLite connection and creates/upgrades the schema. The database lives
/// under %LOCALAPPDATA%\ProgramClock and is the only thing this app persists.
/// </summary>
public sealed class Database : IDisposable
{
    private const int SchemaVersion = 1;

    public SqliteConnection Connection { get; }
    public string Path { get; }

    public Database()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProgramClock");
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "programclock.db");

        Connection = new SqliteConnection($"Data Source={Path}");
        Connection.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA foreign_keys=ON;");
        InitSchema();
    }

    private void InitSchema()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);

            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS categories (
                id         INTEGER PRIMARY KEY,
                name       TEXT NOT NULL,
                is_auto    INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS apps (
                id           INTEGER PRIMARY KEY,
                exe_name     TEXT NOT NULL UNIQUE,
                display_name TEXT,
                exe_path     TEXT,
                publisher    TEXT,
                category_id  INTEGER REFERENCES categories(id),
                tag          TEXT,
                first_seen   TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS usage_daily (
                id       INTEGER PRIMARY KEY,
                app_id   INTEGER NOT NULL REFERENCES apps(id),
                date     TEXT NOT NULL,
                run_ms   INTEGER NOT NULL DEFAULT 0,
                focus_ms INTEGER NOT NULL DEFAULT 0,
                UNIQUE(app_id, date)
            );

            CREATE INDEX IF NOT EXISTS idx_usage_daily_date ON usage_daily(date);

            -- Per-website focused time for browser apps. host is the bare site (e.g. "youtube.com");
            -- only focused time is tracked here because the active tab is all that can be observed.
            CREATE TABLE IF NOT EXISTS site_usage_daily (
                id       INTEGER PRIMARY KEY,
                app_id   INTEGER NOT NULL REFERENCES apps(id),
                host     TEXT NOT NULL,
                date     TEXT NOT NULL,
                focus_ms INTEGER NOT NULL DEFAULT 0,
                UNIQUE(app_id, host, date)
            );

            CREATE INDEX IF NOT EXISTS idx_site_usage_date ON site_usage_daily(date);

            CREATE TABLE IF NOT EXISTS blocked_apps (
                exe_name   TEXT PRIMARY KEY,
                blocked_at TEXT NOT NULL
            );

            -- Websites the user has blocked from per-site tracking, by bare host. Blocking is global
            -- across browsers; the browser's own run/focus totals are unaffected.
            CREATE TABLE IF NOT EXISTS blocked_sites (
                host       TEXT PRIMARY KEY,
                blocked_at TEXT NOT NULL
            );
            """);

        using var check = Connection.CreateCommand();
        check.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var existing = check.ExecuteScalar();
        if (existing is null)
        {
            using var ins = Connection.CreateCommand();
            ins.CommandText = "INSERT INTO schema_version(version) VALUES($v);";
            ins.Parameters.AddWithValue("$v", SchemaVersion);
            ins.ExecuteNonQuery();
        }
    }

    private void Exec(string sql)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => Connection.Dispose();
}
