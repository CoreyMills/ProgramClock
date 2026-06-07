namespace ProgramClock.Data;

/// <summary>Best-effort default categorization. Given an executable name (and publisher), guess a
/// category from a small keyword table. Returns null when nothing matches, leaving the app
/// uncategorized for the user to assign manually.</summary>
public static class CategoryRules
{
    // Category name -> distinctive substrings found in the executable file name (lower-cased, no ".exe").
    // Order matters: the first category with a matching keyword wins.
    private static readonly (string Category, string[] Keywords)[] Map =
    {
        ("Browsers", new[] { "chrome", "firefox", "msedge", "iexplore", "opera", "brave", "vivaldi", "safari" }),
        ("Communication", new[] { "slack", "teams", "discord", "zoom", "skype", "outlook", "thunderbird",
            "telegram", "whatsapp", "signal", "webex" }),
        ("Development", new[] { "devenv", "rider64", "pycharm", "webstorm", "clion", "goland", "rubymine",
            "phpstorm", "idea64", "studio64", "sublime_text", "notepad++", "eclipse", "windowsterminal",
            "powershell", "pwsh", "conhost", "openconsole", "msbuild", "postman", "insomnia", "gitkraken",
            "sourcetree", "ssms", "dbeaver" }),
        ("Design", new[] { "photoshop", "illustrator", "figma", "gimp", "inkscape", "blender", "afterfx",
            "premiere", "lightroom", "coreldraw", "krita", "affinity" }),
        ("Games", new[] { "steam", "epicgameslauncher", "battle.net", "riotclient", "leagueclient",
            "valorant", "origin", "eadesktop", "galaxyclient", "minecraft" }),
        ("Media & Entertainment", new[] { "spotify", "vlc", "wmplayer", "mpc-hc", "foobar2000", "itunes",
            "plex", "obs64", "obs32", "audacity" }),
        ("Productivity", new[] { "winword", "excel", "powerpnt", "onenote", "acrobat", "acrord32", "notion",
            "obsidian", "evernote", "libreoffice", "soffice", "foxit" }),
        ("System & Utilities", new[] { "explorer", "taskmgr", "regedit", "systemsettings", "snippingtool",
            "calc", "mspaint", "notepad", "cleanmgr", "powertoys", "7zfm", "winrar" }),
    };

    public static string? Guess(string exeName, string? publisher)
    {
        if (publisher is not null && publisher.StartsWith("Script (", StringComparison.Ordinal))
            return "Scripts";

        var name = exeName;
        var dot = name.LastIndexOf('.');
        if (dot > 0) name = name[..dot];
        name = name.ToLowerInvariant();

        foreach (var (category, keywords) in Map)
            foreach (var kw in keywords)
                if (name.Contains(kw, StringComparison.Ordinal))
                    return category;

        return null;
    }
}
