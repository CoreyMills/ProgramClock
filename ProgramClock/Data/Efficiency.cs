namespace ProgramClock.Data;

/// <summary>One user-defined efficiency rating: a display name and the minimum weighted-focus score
/// (0..1) needed to earn it. Ratings are kept in ascending threshold order; the lowest acts as the
/// floor (threshold 0), so every score lands on some rating.</summary>
public sealed record EfficiencyRating(string Name, double Threshold);

/// <summary>User-personalizable display names and weights for tags, plus the user-defined set of
/// efficiency ratings. The number of ratings is configurable (add/remove for more or less
/// granularity). Held as an immutable snapshot that the view model swaps wholesale when the user
/// saves.</summary>
public sealed class EfficiencySettings
{
    /// <summary>Tags in the fixed order used by the settings editor and the tooltip breakdown.</summary>
    public static readonly AppTag[] Tags =
        { AppTag.Main, AppTag.Secondary, AppTag.Background, AppTag.Other };

    private readonly IReadOnlyDictionary<AppTag, string> _tagNames;
    private readonly IReadOnlyDictionary<AppTag, double> _tagWeights;
    private readonly IReadOnlyList<EfficiencyRating> _ratings;

    public EfficiencySettings(
        IReadOnlyDictionary<AppTag, string> tagNames,
        IReadOnlyDictionary<AppTag, double> tagWeights,
        IReadOnlyList<EfficiencyRating> ratings)
    {
        _tagNames = tagNames;
        _tagWeights = tagWeights;
        _ratings = NormalizeRatings(ratings);
    }

    public string TagName(AppTag t) =>
        _tagNames.TryGetValue(t, out var n) && !string.IsNullOrWhiteSpace(n) ? n : DefaultTagName(t);

    public double Weight(AppTag t) =>
        _tagWeights.TryGetValue(t, out var w) ? w : DefaultWeight(t);

    /// <summary>The ratings in ascending threshold order (lowest = floor at 0).</summary>
    public IReadOnlyList<EfficiencyRating> Ratings => _ratings;

    /// <summary>Index of the rating a score earns, or -1 when there's no focused time. Picks the
    /// highest rating whose threshold the score clears; the lowest rating is always a valid floor.</summary>
    public int RatingIndexFor(double score, bool hasFocus)
    {
        if (!hasFocus || _ratings.Count == 0) return -1;
        int idx = 0;
        for (int i = 0; i < _ratings.Count; i++)
            if (score >= _ratings[i].Threshold) idx = i;
        return idx;
    }

    public EfficiencyRating? RatingFor(double score, bool hasFocus)
    {
        var i = RatingIndexFor(score, hasFocus);
        return i < 0 ? null : _ratings[i];
    }

    /// <summary>A coarse colour bucket for the dashboard pill — "None", "Low", "Mid" or "High" —
    /// derived from where the earned rating sits in the ordered list, so it works for any count.</summary>
    public string LevelFor(double score, bool hasFocus)
    {
        int i = RatingIndexFor(score, hasFocus);
        if (i < 0) return "None";
        if (_ratings.Count <= 1) return "High";
        if (i == 0) return "Low";
        return i * 2 >= _ratings.Count ? "High" : "Mid"; // top half => High, otherwise the middle band
    }

    public static string DefaultTagName(AppTag t) => t.ToString();

    public static double DefaultWeight(AppTag t) => t switch
    {
        AppTag.Main => 1.0,
        AppTag.Secondary => 0.6,
        AppTag.Background => 0.2,
        _ => 0.0,
    };

    /// <summary>The built-in ratings, used until the user customises them.</summary>
    public static IReadOnlyList<EfficiencyRating> DefaultRatings() => new List<EfficiencyRating>
    {
        new("Poor", 0.0),
        new("Fair", 0.40),
        new("Good", 0.60),
        new("Excellent", 0.80),
    };

    // Guarantee a usable set: fall back to defaults if empty, sort ascending, and pin the lowest to 0.
    private static IReadOnlyList<EfficiencyRating> NormalizeRatings(IReadOnlyList<EfficiencyRating>? ratings)
    {
        if (ratings is null || ratings.Count == 0) return DefaultRatings();
        var sorted = ratings.OrderBy(r => r.Threshold).ToList();
        sorted[0] = sorted[0] with { Threshold = 0.0 };
        return sorted;
    }
}

public static class Efficiency
{
    /// <summary>Weighted share (0..1) of focused time spent on productive tags, using the supplied
    /// per-tag weights. Zero when no focused time was recorded.</summary>
    public static double Score(IEnumerable<(AppTag Tag, long FocusMs)> rows, Func<AppTag, double> weight)
    {
        double total = 0, weighted = 0;
        foreach (var (tag, focus) in rows)
        {
            if (focus <= 0) continue;
            total += focus;
            weighted += focus * weight(tag);
        }
        return total <= 0 ? 0 : weighted / total;
    }
}
