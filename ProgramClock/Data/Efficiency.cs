namespace ProgramClock.Data;

/// <summary>A coarse rating of how focused time was spent, derived from the share of focused time in
/// each <see cref="AppTag"/>. Main work counts fully toward efficiency, Secondary partly, Background
/// little, and Other not at all — though the user can retune those weights in settings.</summary>
public enum EfficiencyTier
{
    None, // no focused time recorded in the range
    Poor,
    Fair,
    Good,
    Excellent,
}

/// <summary>User-personalizable display names and weights for tags and efficiency tiers. The enum
/// identity (the value stored in the DB) never changes; only the label shown in the UI and, for
/// tags, the weight applied to the efficiency score are configurable. Held as an immutable snapshot
/// that the view model swaps wholesale when the user saves.</summary>
public sealed class EfficiencySettings
{
    /// <summary>Tags in the fixed order used by the settings editor and the tooltip breakdown.</summary>
    public static readonly AppTag[] Tags =
        { AppTag.Main, AppTag.Secondary, AppTag.Background, AppTag.Other };

    /// <summary>Tiers that carry a user-visible name (<see cref="EfficiencyTier.None"/> always
    /// renders as a dash).</summary>
    public static readonly EfficiencyTier[] NamedTiers =
        { EfficiencyTier.Poor, EfficiencyTier.Fair, EfficiencyTier.Good, EfficiencyTier.Excellent };

    private readonly IReadOnlyDictionary<AppTag, string> _tagNames;
    private readonly IReadOnlyDictionary<AppTag, double> _tagWeights;
    private readonly IReadOnlyDictionary<EfficiencyTier, string> _tierNames;

    public EfficiencySettings(
        IReadOnlyDictionary<AppTag, string> tagNames,
        IReadOnlyDictionary<AppTag, double> tagWeights,
        IReadOnlyDictionary<EfficiencyTier, string> tierNames)
    {
        _tagNames = tagNames;
        _tagWeights = tagWeights;
        _tierNames = tierNames;
    }

    public string TagName(AppTag t) =>
        _tagNames.TryGetValue(t, out var n) && !string.IsNullOrWhiteSpace(n) ? n : DefaultTagName(t);

    public double Weight(AppTag t) =>
        _tagWeights.TryGetValue(t, out var w) ? w : DefaultWeight(t);

    public string TierName(EfficiencyTier t) => t == EfficiencyTier.None
        ? "—"
        : _tierNames.TryGetValue(t, out var n) && !string.IsNullOrWhiteSpace(n) ? n : DefaultTierName(t);

    public static string DefaultTagName(AppTag t) => t.ToString();
    public static string DefaultTierName(EfficiencyTier t) => t.ToString();

    public static double DefaultWeight(AppTag t) => t switch
    {
        AppTag.Main => 1.0,
        AppTag.Secondary => 0.6,
        AppTag.Background => 0.2,
        _ => 0.0,
    };
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

    public static EfficiencyTier ToTier(double score, bool hasFocus) => !hasFocus
        ? EfficiencyTier.None
        : score >= 0.80 ? EfficiencyTier.Excellent
        : score >= 0.60 ? EfficiencyTier.Good
        : score >= 0.40 ? EfficiencyTier.Fair
        : EfficiencyTier.Poor;
}
