using System.Text.Json.Serialization;

namespace RunnersDiary.Api.Features.Insights;

// Wire contract of GET/POST /api/insights/summary (M6 plan section 3.2). Text carries {key} placeholders; the client resolves them
// from `values` with the existing formatters. Enum members have explicit lower camel case names.

public enum SummaryStateDto
{
    [JsonStringEnumMemberName("none")]
    None,

    [JsonStringEnumMemberName("fresh")]
    Fresh,

    [JsonStringEnumMemberName("stale")]
    Stale,
}

public enum SummarySourceDto
{
    [JsonStringEnumMemberName("model")]
    Model,

    [JsonStringEnumMemberName("demo")]
    Demo,

    [JsonStringEnumMemberName("template")]
    Template,
}

public enum GenerationAvailabilityDto
{
    [JsonStringEnumMemberName("available")]
    Available,

    [JsonStringEnumMemberName("notConfigured")]
    NotConfigured,

    [JsonStringEnumMemberName("quotaExhausted")]
    QuotaExhausted,

    /// <summary>A template period (no sessions, planned only, very few sessions): there is nothing to generate.</summary>
    [JsonStringEnumMemberName("notNeeded")]
    NotNeeded,
}

public enum QuotaScopeDto
{
    /// <summary>Users: generations in the last 24 hours.</summary>
    [JsonStringEnumMemberName("rolling24h")]
    Rolling24h,

    /// <summary>Guests: generations since the guest session started.</summary>
    [JsonStringEnumMemberName("account")]
    Account,
}

/// <summary>The unit of a resolved value; the client picks the formatter by it. Names match the fact sheet units.</summary>
public enum SummaryUnitDto
{
    [JsonStringEnumMemberName("meters")]
    Meters,

    [JsonStringEnumMemberName("seconds")]
    Seconds,

    [JsonStringEnumMemberName("load")]
    Load,

    [JsonStringEnumMemberName("ratio")]
    Ratio,

    [JsonStringEnumMemberName("percent")]
    Percent,

    [JsonStringEnumMemberName("share")]
    Share,

    [JsonStringEnumMemberName("secondsPerKm")]
    SecondsPerKm,

    [JsonStringEnumMemberName("secondsPerKmPerWeek")]
    SecondsPerKmPerWeek,

    [JsonStringEnumMemberName("count")]
    Count,

    [JsonStringEnumMemberName("days")]
    Days,

    [JsonStringEnumMemberName("date")]
    Date,

    [JsonStringEnumMemberName("band")]
    Band,

    [JsonStringEnumMemberName("runType")]
    RunType,

    [JsonStringEnumMemberName("raceCategory")]
    RaceCategory,

    [JsonStringEnumMemberName("raceStatus")]
    RaceStatus,

    [JsonStringEnumMemberName("flag")]
    Flag,

    [JsonStringEnumMemberName("stars")]
    Stars,
}

/// <summary>Body of <c>POST /api/insights/summary</c>. Unknown members are rejected (400).</summary>
public sealed record GenerateSummaryRequest
{
    /// <summary>First day of the period (yyyy-MM-dd).</summary>
    public DateOnly? From { get; init; }

    /// <summary>Last day of the period (yyyy-MM-dd).</summary>
    public DateOnly? To { get; init; }

    /// <summary>none, previous (default) or previousYear; case-sensitive.</summary>
    public string? Compare { get; init; }

    /// <summary>Language of the summary; only "en" (default) in this version.</summary>
    public string? Lang { get; init; }

    /// <summary>True to replace a fresh stored summary with a new generation (uses quota); false (default) returns a fresh one as is.</summary>
    public bool Regenerate { get; init; }
}

public sealed record SummaryPeriodDto
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    public required string Compare { get; init; }

    public required string Lang { get; init; }
}

public sealed record SummaryTextDto
{
    /// <summary>Prose with {key} placeholders for every number, date and category.</summary>
    public required string Text { get; init; }

    /// <summary>The keys cited in <see cref="Text"/>, in order of appearance.</summary>
    public required IReadOnlyList<string> Keys { get; init; }
}

public sealed record SummaryValueDto
{
    /// <summary>The value when it is numeric (metres, seconds, AU, a ratio, a percent, a count...); otherwise null.</summary>
    public required double? Number { get; init; }

    /// <summary>The value when it is not numeric (a date as yyyy-MM-dd, a code, a band, "true"/"false"); otherwise null.</summary>
    public required string? Text { get; init; }

    public required SummaryUnitDto Unit { get; init; }
}

public sealed record InsightsSummaryDto
{
    public required SummarySourceDto Source { get; init; }

    /// <summary>UTC; model summaries only.</summary>
    public required DateTimeOffset? CreatedAt { get; init; }

    public required int MetricsVersion { get; init; }

    public required int FactSheetVersion { get; init; }

    /// <summary>Model summaries only.</summary>
    public required int? PromptVersion { get; init; }

    /// <summary>The model id the provider reported (or the deployment name); model summaries only.</summary>
    public required string? Model { get; init; }

    public required SummaryTextDto Headline { get; init; }

    public required IReadOnlyList<SummaryTextDto> WentWell { get; init; }

    public required IReadOnlyList<SummaryTextDto> WatchOuts { get; init; }

    public required SummaryTextDto? Suggestion { get; init; }

    public required IReadOnlyList<SummaryTextDto> DataNotes { get; init; }

    /// <summary>Every key cited by any text, resolved from the metrics the summary was written for.</summary>
    public required IReadOnlyDictionary<string, SummaryValueDto> Values { get; init; }
}

public sealed record SummaryGenerationDto
{
    public required GenerationAvailabilityDto Availability { get; init; }

    /// <summary>Generations counted against the account quota; null when not configured or not needed.</summary>
    public required int? Used { get; init; }

    /// <summary>The account quota; null when not configured or not needed.</summary>
    public required int? Limit { get; init; }

    /// <summary>UTC; when the next generation frees up. Null for guests (the quota lasts for the session) and when not exhausted.</summary>
    public required DateTimeOffset? ResetsAt { get; init; }

    public required QuotaScopeDto? Scope { get; init; }
}

public sealed record InsightsSummaryResponse
{
    public required SummaryPeriodDto Period { get; init; }

    public required SummaryStateDto State { get; init; }

    public required InsightsSummaryDto? Summary { get; init; }

    public required SummaryGenerationDto Generation { get; init; }
}
