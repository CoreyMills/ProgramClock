using Microsoft.Data.Sqlite;

namespace ProgramClock.Data;

/// <summary>CRUD for categories and the app -> category assignment. Shares the single SQLite
/// connection with the other repositories and locks on it so writes from the UI thread don't collide
/// with the tracker's flush thread.</summary>
public sealed class CategoryRepository
{
    private readonly SqliteConnection _conn;

    public CategoryRepository(SqliteConnection conn) => _conn = conn;

    public List<CategoryRecord> List()
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, is_auto, default_tag FROM categories ORDER BY name COLLATE NOCASE;";
            var list = new List<CategoryRecord>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new CategoryRecord
                {
                    Id = r.GetInt64(0),
                    Name = r.GetString(1),
                    IsAuto = r.GetInt64(2) != 0,
                    DefaultTag = r.IsDBNull(3) ? null : r.GetString(3).ParseTagOrNull(),
                });
            return list;
        }
    }

    /// <summary>Find a category by name (case-insensitive) or create it. Used both by manual creation
    /// and by the auto-categorizer when it first needs a given category.</summary>
    public long EnsureByName(string name, bool isAuto)
    {
        lock (_conn)
        {
            using var sel = _conn.CreateCommand();
            sel.CommandText = "SELECT id FROM categories WHERE name=$n COLLATE NOCASE LIMIT 1;";
            sel.Parameters.AddWithValue("$n", name);
            if (sel.ExecuteScalar() is { } found) return Convert.ToInt64(found);

            using var ins = _conn.CreateCommand();
            ins.CommandText =
                "INSERT INTO categories(name, is_auto, created_at) VALUES($n,$a,$t); " +
                "SELECT last_insert_rowid();";
            ins.Parameters.AddWithValue("$n", name);
            ins.Parameters.AddWithValue("$a", isAuto ? 1 : 0);
            ins.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            return Convert.ToInt64(ins.ExecuteScalar());
        }
    }

    public long Create(string name) => EnsureByName(name, isAuto: false);

    public void Rename(long id, string name)
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE categories SET name=$n, is_auto=0 WHERE id=$id;";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(long id)
    {
        lock (_conn)
        {
            using var clear = _conn.CreateCommand();
            clear.CommandText = "UPDATE apps SET category_id=NULL WHERE category_id=$id;";
            clear.Parameters.AddWithValue("$id", id);
            clear.ExecuteNonQuery();

            using var del = _conn.CreateCommand();
            del.CommandText = "DELETE FROM categories WHERE id=$id;";
            del.Parameters.AddWithValue("$id", id);
            del.ExecuteNonQuery();
        }
    }

    public void AssignApp(long appId, long? categoryId)
    {
        lock (_conn)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE apps SET category_id=$c WHERE id=$a;";
                cmd.Parameters.AddWithValue("$c", (object?)categoryId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$a", appId);
                cmd.ExecuteNonQuery();
            }
            ApplyCategoryDefaultTag(appId, categoryId);
        }
    }

    /// <summary>Set (or clear, with null) a category's default tag. Not retroactive: it only affects apps
    /// moved into the category afterward.</summary>
    public void SetDefaultTag(long categoryId, AppTag? tag)
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE categories SET default_tag=$t WHERE id=$id;";
            cmd.Parameters.AddWithValue("$t", (object?)tag?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", categoryId);
            cmd.ExecuteNonQuery();
        }
    }

    // When an app is moved into a category that defines a default tag, adopt it — but only while the app
    // is still on the default 'Other' tag. A tag the user set deliberately (Main/Secondary/Background) is
    // preserved across category changes. Assumes the caller holds the connection lock.
    private void ApplyCategoryDefaultTag(long appId, long? categoryId)
    {
        if (categoryId is not long catId) return;   // Uncategorized: nothing to apply

        AppTag? def;
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = "SELECT default_tag FROM categories WHERE id=$id;";
            sel.Parameters.AddWithValue("$id", catId);
            var raw = sel.ExecuteScalar();
            def = raw is null or DBNull ? null : ((string)raw).ParseTagOrNull();
        }
        if (def is not AppTag tag) return;          // category has no default tag

        AppTag current;
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = "SELECT tag FROM apps WHERE id=$id;";
            sel.Parameters.AddWithValue("$id", appId);
            current = (sel.ExecuteScalar() as string).ParseTag();   // null/unset -> Other
        }
        if (current == AppTag.Other) AssignTag(appId, tag);
    }

    /// <summary>The app's current tag, defaulting to Other when unset.</summary>
    public AppTag GetTag(long appId)
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(tag,'Other') FROM apps WHERE id=$a;";
            cmd.Parameters.AddWithValue("$a", appId);
            return (cmd.ExecuteScalar() as string).ParseTag();
        }
    }

    /// <summary>Set an app's tag (Main/Secondary/Background/Other), stored as the enum name.</summary>
    public void AssignTag(long appId, AppTag tag)
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE apps SET tag=$t WHERE id=$a;";
            cmd.Parameters.AddWithValue("$t", tag.ToString());
            cmd.Parameters.AddWithValue("$a", appId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Assign a category to an app by exe name, resolving (or inserting) its row first — so the
    /// dashboard can categorize an app even before its first flush inserts it.</summary>
    public void AssignAppByExe(string exeName, long? categoryId)
    {
        lock (_conn) AssignApp(EnsureAppId(exeName), categoryId);
    }

    /// <summary>Assign a tag to an app by exe name (resolving/inserting its row first).</summary>
    public void AssignTagByExe(string exeName, AppTag tag)
    {
        lock (_conn) AssignTag(EnsureAppId(exeName), tag);
    }

    // Find the app id for an exe, inserting a bare row (exe + first_seen) if it isn't tracked yet.
    private long EnsureAppId(string exeName)
    {
        using var sel = _conn.CreateCommand();
        sel.CommandText = "SELECT id FROM apps WHERE exe_name=$e;";
        sel.Parameters.AddWithValue("$e", exeName);
        if (sel.ExecuteScalar() is { } found) return Convert.ToInt64(found);

        using var ins = _conn.CreateCommand();
        ins.CommandText = "INSERT INTO apps(exe_name, first_seen) VALUES($e,$t); SELECT last_insert_rowid();";
        ins.Parameters.AddWithValue("$e", exeName);
        ins.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        return Convert.ToInt64(ins.ExecuteScalar());
    }

    /// <summary>One-time pass that applies the auto-categorization rules to apps that have no category
    /// yet (e.g. rows recorded before categories existed). Returns how many were assigned.</summary>
    public int BackfillUncategorized()
    {
        lock (_conn)
        {
            var pending = new List<(long Id, string Exe, string? Publisher)>();
            using (var sel = _conn.CreateCommand())
            {
                sel.CommandText = "SELECT id, exe_name, publisher FROM apps WHERE category_id IS NULL;";
                using var r = sel.ExecuteReader();
                while (r.Read())
                    pending.Add((r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
            }

            int assigned = 0;
            foreach (var (id, exe, publisher) in pending)
            {
                var name = CategoryRules.Guess(exe, publisher);
                if (name is null) continue;
                AssignApp(id, EnsureByName(name, isAuto: true));
                assigned++;
            }
            return assigned;
        }
    }

    public List<AppCategoryRow> ListApps()
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "SELECT id, exe_name, COALESCE(display_name, exe_name), category_id, " +
                "COALESCE(tag, 'Other') " +
                "FROM apps ORDER BY COALESCE(display_name, exe_name) COLLATE NOCASE;";
            var list = new List<AppCategoryRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new AppCategoryRow
                {
                    AppId = r.GetInt64(0),
                    ExeName = r.GetString(1),
                    DisplayName = r.GetString(2),
                    CategoryId = r.IsDBNull(3) ? null : r.GetInt64(3),
                    Tag = r.GetString(4).ParseTag(),
                });
            return list;
        }
    }
}
