namespace ProgramClock.Data;

/// <summary>A tracked application, keyed by its executable file name.</summary>
public sealed class AppRecord
{
    public long Id { get; init; }
    public required string ExeName { get; init; }
    public string? DisplayName { get; set; }
    public string? ExePath { get; set; }
    public string? Publisher { get; set; }
    public long? CategoryId { get; set; }
    public string? Tag { get; set; }
}

/// <summary>How an app's focused time counts toward efficiency. Stored as the enum name in
/// <c>apps.tag</c>; a missing value reads back as <see cref="Other"/>.</summary>
public enum AppTag
{
    Main,
    Secondary,
    Background,
    Other,
}

public static class AppTagExtensions
{
    public static AppTag ParseTag(this string? value) =>
        Enum.TryParse<AppTag>(value, ignoreCase: true, out var t) ? t : AppTag.Other;
}

/// <summary>Aggregated usage for one app over a queried date range.</summary>
public sealed class UsageRow
{
    public required string ExeName { get; init; }
    public string DisplayName { get; init; } = "";
    public long RunMs { get; init; }
    public long FocusMs { get; init; }
    public string CategoryName { get; init; } = "";
    public AppTag Tag { get; init; } = AppTag.Other;
}

/// <summary>Aggregated focused time for one website under a browser app over a queried date range.</summary>
public sealed class SiteUsageRow
{
    public required string ExeName { get; init; }
    public required string Host { get; init; }
    public long FocusMs { get; init; }
}

/// <summary>A usage category. Auto categories are seeded by the publisher/exe rules; manual ones are
/// created by the user.</summary>
public sealed class CategoryRecord
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public bool IsAuto { get; init; }
}

/// <summary>An app and its current category, for the categories management page.</summary>
public sealed class AppCategoryRow
{
    public required long AppId { get; init; }
    public required string ExeName { get; init; }
    public string DisplayName { get; init; } = "";
    public long? CategoryId { get; init; }
    public AppTag Tag { get; init; } = AppTag.Other;
}

/// <summary>The date range a dashboard query covers.</summary>
public enum DateRange
{
    Today,
    Week,
    Month,
    All,
}
