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
        Migrate();
    }

    /// <summary>
    /// Brings the on-disk schema up to <see cref="SchemaVersion"/> by running ordered migration
    /// steps from whatever version the file is currently at.
    /// <para>
    /// This is what guarantees a user's settings (and history) survive app updates. The updater only
    /// swaps <c>ProgramClock.exe</c>; this database file in <c>%LOCALAPPDATA%</c> is never touched by
    /// it. Migrations here are required to be <b>strictly additive</b> — only <c>CREATE … IF NOT
    /// EXISTS</c> and <c>ALTER TABLE … ADD COLUMN</c>, never <c>DROP</c> or destructive rewrites — so
    /// upgrading can introduce new tables/columns/settings without resetting anything that already
    /// exists. A user's stored setting changes only if a future migration deliberately rewrites that
    /// specific row.
    /// </para>
    /// </summary>
    private void Migrate()
    {
        Exec("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);");

        int current = GetSchemaVersion();   // 0 for a brand-new database file

        // Each step is additive and idempotent. To evolve the schema later, bump SchemaVersion and add
        // a new `if (current < N) { … }` block below — do NOT edit an already-shipped step, and never
        // drop/recreate a table that holds user data.
        if (current < 1) MigrateToV1();

        if (current != SchemaVersion) SetSchemaVersion(SchemaVersion);
    }

    /// <summary>v1 baseline: the original table set. Safe to run against an existing v1 database.</summary>
    private void MigrateToV1()
    {
        Exec("""
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
    }

    /// <summary>Returns the schema version recorded in the file, or 0 if none has been written yet.</summary>
    private int GetSchemaVersion()
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var existing = cmd.ExecuteScalar();
        return existing is null or DBNull ? 0 : Convert.ToInt32(existing);
    }

    /// <summary>Records the schema version, replacing any existing value (keeps a single row).</summary>
    private void SetSchemaVersion(int version)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM schema_version; INSERT INTO schema_version(version) VALUES($v);";
        cmd.Parameters.AddWithValue("$v", version);
        cmd.ExecuteNonQuery();
    }

    private void Exec(string sql)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => Connection.Dispose();
}
