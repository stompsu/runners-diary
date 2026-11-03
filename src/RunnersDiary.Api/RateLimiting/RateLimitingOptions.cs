namespace RunnersDiary.Api.RateLimiting;

/// <summary>Rate limits (<c>RateLimiting</c> section). Defaults are ADR 0011 §6; Development raises the guest-create limit.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public GuestCreateLimit GuestCreate { get; set; } = new();

    public GeneralLimit General { get; set; } = new();

    /// <summary>
    /// Sliding window per client for <c>POST /api/guest-sessions</c> (IPv4 address or IPv6 /64), plus one global window over all
    /// clients as a backstop against rotating addresses.
    /// </summary>
    public sealed class GuestCreateLimit
    {
        public int PermitLimit { get; set; } = 5;

        public TimeSpan Window { get; set; } = TimeSpan.FromHours(1);

        /// <summary>Guest creations per <see cref="GlobalWindow"/> across all clients.</summary>
        public int GlobalPermitLimit { get; set; } = 60;

        public TimeSpan GlobalWindow { get; set; } = TimeSpan.FromHours(1);
    }

    /// <summary>Token bucket per caller (user id, else client IP) for all authenticated <c>/api</c> traffic.</summary>
    public sealed class GeneralLimit
    {
        public int TokenLimit { get; set; } = 120;

        public int TokensPerPeriod { get; set; } = 60;

        public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(30);
    }
}
