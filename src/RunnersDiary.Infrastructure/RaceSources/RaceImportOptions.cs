using RunnersDiary.Domain.Races;

namespace RunnersDiary.Infrastructure.RaceSources;

/// <summary><c>RaceImport</c> section (M8 plan §4.5, ADR 0035 §4-5). Everything is off by default; Production switches come from the Bicep parameters.</summary>
public sealed class RaceImportOptions
{
    public const string SectionName = "RaceImport";

    /// <summary>The identifying User-Agent of every request (ADR 0035 §4); the project page is the contact URL.</summary>
    public const string UserAgent = "RunnersDiaryRaceImport/1.0 (+https://github.com/stompsu/runners-diary)";

    /// <summary>The robots.txt product token of <see cref="UserAgent"/>.</summary>
    public const string ProductToken = "RunnersDiaryRaceImport";

    /// <summary>The lowest allowed <see cref="MinInterval"/>, enforced at startup.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    /// <summary>Global kill switch: false means the job exits 0 without opening a SQL connection.</summary>
    public bool Enabled { get; set; }

    public SourceSwitches Sources { get; set; } = new();

    /// <summary>Gap between two requests to one host.</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(1500);

    public int MaxListingPages { get; set; } = 40;

    public int MaxDetailPages { get; set; } = 150;

    /// <summary>A detail page not read for this long is read again.</summary>
    public TimeSpan DetailRefreshAfter { get; set; } = TimeSpan.FromDays(28);

    public int RemoveAfterMissedScans { get; set; } = 2;

    /// <summary>Races dated more than this many days in the past are deleted at the end of a successful run.</summary>
    public int RetainPastDays { get; set; } = 180;

    /// <summary>Per request, including reading the body.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Response body cap in bytes (default 2 MB).</summary>
    public int MaxResponseBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Whether <paramref name="source"/> is switched on (<c>RaceImport:Sources:&lt;Id&gt;:Enabled</c>); the global switch is checked separately.</summary>
    public bool IsEnabled(RaceSource source) => source switch
    {
        RaceSource.RunningLife => Sources.RunningLife.Enabled,
        RaceSource.Demo => Sources.Demo.Enabled,
        _ => false,
    };

    /// <summary>The settings problems, by key; empty when valid. Messages name keys, never values.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (MinInterval < MinimumInterval)
        {
            problems.Add($"{SectionName}:MinInterval must be at least {MinimumInterval.TotalSeconds:0} second.");
        }

        if (MaxListingPages is < 1 or > 200)
        {
            problems.Add($"{SectionName}:MaxListingPages must be between 1 and 200.");
        }

        if (MaxDetailPages is < 0 or > 1000)
        {
            problems.Add($"{SectionName}:MaxDetailPages must be between 0 and 1000.");
        }

        if (DetailRefreshAfter < TimeSpan.FromDays(1))
        {
            problems.Add($"{SectionName}:DetailRefreshAfter must be at least one day.");
        }

        if (RemoveAfterMissedScans is < 1 or > 10)
        {
            problems.Add($"{SectionName}:RemoveAfterMissedScans must be between 1 and 10.");
        }

        if (RetainPastDays is < 0 or > 3650)
        {
            problems.Add($"{SectionName}:RetainPastDays must be between 0 and 3650.");
        }

        if (RequestTimeout < TimeSpan.FromSeconds(1) || RequestTimeout > TimeSpan.FromSeconds(120))
        {
            problems.Add($"{SectionName}:RequestTimeout must be between 1 and 120 seconds.");
        }

        if (MaxResponseBytes is < 1024 or > 8 * 1024 * 1024)
        {
            problems.Add($"{SectionName}:MaxResponseBytes must be between 1 KB and 8 MB.");
        }

        return problems;
    }

    public sealed class SourceSwitches
    {
        public SourceSwitch RunningLife { get; set; } = new();

        /// <summary>The synthetic demo source: Development, tests and CI only (the Production validator rejects it).</summary>
        public SourceSwitch Demo { get; set; } = new();
    }

    public sealed class SourceSwitch
    {
        public bool Enabled { get; set; }
    }
}
