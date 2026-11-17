namespace RunnersDiary.Infrastructure.Guests;

/// <summary>Guest lifecycle settings (<c>Guests</c> section); defaults are ADR 0011.</summary>
public sealed class GuestOptions
{
    public const string SectionName = "Guests";

    /// <summary>Maximum number of unexpired guest accounts; creation beyond it is answered with 503.</summary>
    public int MaxActive { get; set; } = 200;

    /// <summary>Process-wide number of guest seedings that may run at once.</summary>
    public int SeedingConcurrency { get; set; } = 2;

    /// <summary>How long a creation waits for a seeding slot before giving up with 503.</summary>
    public TimeSpan SeedingWaitTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Directory holding <c>runs.json</c> and <c>gpx/</c>; relative paths are resolved against the application base directory.</summary>
    public string DemoDataPath { get; set; } = "seed/demo";

    /// <summary>
    /// When true, the startup check fails the host if a supported non-English language has demo seed texts without an entry in
    /// <c>seed/demo/i18n/&lt;lang&gt;.json</c> (ADR 0042 §7). Off until the Polish map is complete (M10 step B4 turns it on); missing entries are logged as a warning meanwhile.
    /// </summary>
    public bool RequireCompleteDemoTranslations { get; set; }

    public CleanupOptions Cleanup { get; set; } = new();

    public sealed class CleanupOptions
    {
        /// <summary>Master switch for the background service and the opportunistic run on guest creation.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Guests are removed once <c>ExpiresAt</c> is this far in the past (more than any token clock skew).</summary>
        public TimeSpan Grace { get; set; } = TimeSpan.FromHours(1);

        public int BatchSize { get; set; } = 50;

        public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);

        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>Minimum time between opportunistic runs triggered by guest creation.</summary>
        public TimeSpan OpportunisticMinInterval { get; set; } = TimeSpan.FromMinutes(10);
    }
}
