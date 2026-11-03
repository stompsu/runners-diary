using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Time;

namespace RunnersDiary.Domain.Users;

/// <summary>An application user: a real (Entra External ID) account or a 24 h guest.</summary>
public sealed class User
{
    public const string DefaultEntraTimeZoneId = "Etc/UTC";
    public const string GuestTimeZoneId = "Europe/Warsaw";
    public static readonly TimeSpan GuestLifetime = TimeSpan.FromHours(24);
    public const int DisplayNameMaxLength = 100;

    private User()
    {
    }

    public Guid Id { get; private set; }

    public UserKind Kind { get; private set; }

    /// <summary>Entra <c>tid</c>; null for guests.</summary>
    public Guid? ExternalTenantId { get; private set; }

    /// <summary>Entra <c>oid</c>; null for guests.</summary>
    public Guid? ExternalObjectId { get; private set; }

    public string? DisplayName { get; private set; }

    public string TimeZoneId { get; private set; } = string.Empty;

    public bool TimeZoneIsDefault { get; private set; }

    public UnitSystem UnitSystem { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Guests only: equals the guest token expiry.</summary>
    public DateTime? ExpiresAt { get; private set; }

    public static User CreateGuest(DateTimeOffset now, Guid? id = null)
    {
        var created = Truncate(now);
        return new User
        {
            Id = id ?? Guid.CreateVersion7(),
            Kind = UserKind.Guest,
            TimeZoneId = GuestTimeZoneId,
            TimeZoneIsDefault = false,
            UnitSystem = UnitSystem.Metric,
            CreatedAt = created,
            ExpiresAt = created + GuestLifetime,
        };
    }

    public static User CreateEntra(Guid tenantId, Guid objectId, string? displayName, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || objectId == Guid.Empty)
        {
            throw new DomainRuleException("Entra tenant and object ids are required.");
        }

        return new User
        {
            Id = Guid.CreateVersion7(),
            Kind = UserKind.User,
            ExternalTenantId = tenantId,
            ExternalObjectId = objectId,
            DisplayName = NormalizeDisplayName(displayName),
            TimeZoneId = DefaultEntraTimeZoneId,
            TimeZoneIsDefault = true,
            UnitSystem = UnitSystem.Metric,
            CreatedAt = Truncate(now),
        };
    }

    public void UpdateProfile(string? displayName, string timeZoneId, UnitSystem unitSystem)
    {
        if (!IanaTimeZone.IsValid(timeZoneId))
        {
            throw new DomainRuleException([new DomainError("timeZoneId", "Must be a valid IANA time zone id.")]);
        }

        if (!Enum.IsDefined(unitSystem))
        {
            throw new DomainRuleException([new DomainError("unitSystem", "Unknown unit system.")]);
        }

        DisplayName = NormalizeDisplayName(displayName);
        TimeZoneId = timeZoneId;
        TimeZoneIsDefault = false;
        UnitSystem = unitSystem;
    }

    private static string? NormalizeDisplayName(string? displayName)
    {
        var trimmed = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > DisplayNameMaxLength
            ? throw new DomainRuleException([new DomainError("displayName", $"Must be at most {DisplayNameMaxLength} characters.")])
            : trimmed;
    }

    private static DateTime Truncate(DateTimeOffset value) =>
        new(value.UtcDateTime.Ticks - (value.UtcDateTime.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
}
