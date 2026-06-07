using System.Windows.Automation;

namespace ProgramClock.Interop;

/// <summary>Reads the URL showing in a browser window's address bar via UI Automation, then reduces
/// it to a bare host (e.g. "youtube.com") so every page on the same site groups under one entry.
/// Works for the focused window only — that's all the active tab exposes.</summary>
internal static class BrowserUrlReader
{
    // The address bar is an Edit control that exposes a ValuePattern carrying the current URL. It's
    // the same window each tick, so cache the resolved element per hwnd: re-walking a browser's huge
    // automation tree every focus tick is the expensive part, and the cached element keeps working as
    // the user navigates within the same window. A stale entry (window closed / element invalidated)
    // throws on read and is dropped, forcing a fresh find.
    private static readonly Dictionary<IntPtr, AutomationElement> Cache = new();

    // Chromium browsers surface the URL on an Edit control (the address bar). Firefox's address bar
    // isn't reported as a plain Edit, but like every URL-bearing control it exposes a ValuePattern —
    // so a second, broader pass catches it (and any other browser whose bar isn't an Edit).
    private static readonly Condition EditCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
    private static readonly Condition ValueCondition =
        new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true);

    /// <summary>The host of the focused browser window's active tab, or null when it can't be read
    /// (no address bar, a search term rather than a URL, access denied, etc.).</summary>
    public static string? GetActiveHost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        try
        {
            if (Cache.TryGetValue(hwnd, out var cached))
            {
                var url = ReadValue(cached);
                if (url is not null) return HostOf(url);
                Cache.Remove(hwnd);
            }

            var root = AutomationElement.FromHandle(hwnd);
            if (root is null) return null;

            // Fast path: the address bar is an Edit in Chromium browsers. Broad fallback: any control
            // exposing a ValuePattern, which is how Firefox's address bar shows up. The address bar
            // lives in the toolbar (ahead of page content in document order), so the first candidate
            // whose value parses to a real host is the URL rather than some in-page text field.
            return FindHost(root, EditCondition, hwnd) ?? FindHost(root, ValueCondition, hwnd);
        }
        catch
        {
            Cache.Remove(hwnd);
            return null;
        }
    }

    // Scan the descendants matching a condition and return the first value that parses to a host,
    // caching the element that produced it so later ticks skip the tree walk.
    private static string? FindHost(AutomationElement root, Condition condition, IntPtr hwnd)
    {
        foreach (AutomationElement el in root.FindAll(TreeScope.Descendants, condition))
        {
            var url = ReadValue(el);
            if (url is null) continue;
            var host = HostOf(url);
            if (host is null) continue;
            Cache[hwnd] = el;
            return host;
        }
        return null;
    }

    private static string? ReadValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            {
                var value = ((ValuePattern)pattern).Current.Value;
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        catch
        {
            // Element invalidated (navigation/close) — caller drops it from the cache.
        }
        return null;
    }

    /// <summary>Reduce a raw address-bar string to a clean host: prepend a scheme when the bar hides
    /// it ("youtube.com/watch…"), strip "www.", and reject anything that isn't a real hostname (a typed
    /// search phrase, a single bare word, etc.) so those don't pollute the breakdown.</summary>
    public static string? HostOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var s = url.Trim();
        if (s.Contains(' ')) return null;                 // a search phrase, not a URL
        if (!s.Contains("://")) s = "http://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host;
        if (string.IsNullOrEmpty(host)) return null;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        // Require a dotted hostname (or localhost) so bare words from the omnibox are ignored.
        if (!host.Contains('.') && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return null;
        return host.ToLowerInvariant();
    }
}
