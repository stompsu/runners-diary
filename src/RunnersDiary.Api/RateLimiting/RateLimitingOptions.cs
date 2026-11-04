namespace RunnersDiary.Api.RateLimiting;

/// <summary>Rate limits (<c>RateLimiting</c> section). Defaults are ADR 0011 §6; Development raises the guest-create limit.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public GuestCreateLimit GuestCreate { get; set; } = new();

    public GeneralLimit General { get; set; } = new();

    /// <summary>GPX uploads per caller, sliding window (ADR 0020 §7).</summary>
    public PerCallerLimit GpxUpload { get; set; } = new() { UserPermitLimit = 30, GuestPermitLimit = 10, Window = TimeSpan.FromHours(1) };

    /// <summary>Place searches per caller, sliding window (ADR 0022 §4).</summary>
    public PerCallerLimit Geocode { get; set; } = new() { UserPermitLimit = 20, GuestPermitLimit = 10, Window = TimeSpan.FromMinutes(1) };

    /// <summary>Azure Maps SAS token requests per caller (ADR 0033: users 30, guests 10 per hour).</summary>
    public PerCallerLimit MapsToken { get; set; } = new() { UserPermitLimit = 30, GuestPermitLimit = 10, Window = TimeSpan.FromHours(1) };

    /// <summary>AI summary generations per caller, burst guard on top of the durable quotas (M6 plan Q4: users 6, guests 3 per 10 minutes).</summary>
    public PerCallerLimit AiSummary { get; set; } = new() { UserPermitLimit = 6, GuestPermitLimit = 3, Window = TimeSpan.FromMinutes(10) };

    /// <summary>GET /api/health, anonymous or not (per client IP when anonymous): a light guard, the anonymous answer costs no SQL.</summary>
    public PerCallerLimit Health { get; set; } = new() { UserPermitLimit = 30, GuestPermitLimit = 30, Window = TimeSpan.FromMinutes(1) };

    /// <summary>Sliding window per caller (user id); guests get their own, lower, permit count.</summary>
    public sealed class PerCallerLimit
    {
        public int UserPermitLimit { get; set; }

        public int GuestPermitLimit { get; set; }

        public TimeSpan Window { get; set; }
    }

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
