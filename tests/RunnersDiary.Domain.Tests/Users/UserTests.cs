using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Users;

namespace RunnersDiary.Domain.Tests.Users;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Guest_expires_after_24_hours_and_uses_warsaw()
    {
        var guest = User.CreateGuest(Now);

        Assert.Equal(UserKind.Guest, guest.Kind);
        Assert.Null(guest.ExternalObjectId);
        Assert.Equal("Europe/Warsaw", guest.TimeZoneId);
        Assert.Equal(Now.UtcDateTime.AddHours(24), guest.ExpiresAt);
    }

    [Fact]
    public void Entra_user_starts_in_utc_with_default_zone_flag()
    {
        var tenant = Guid.NewGuid();
        var oid = Guid.NewGuid();

        var user = User.CreateEntra(tenant, oid, "  Ada  ", Now);

        Assert.Equal(UserKind.User, user.Kind);
        Assert.Equal("Etc/UTC", user.TimeZoneId);
        Assert.True(user.TimeZoneIsDefault);
        Assert.Equal("Ada", user.DisplayName);
        Assert.Null(user.ExpiresAt);
        Assert.Equal(tenant, user.ExternalTenantId);
    }

    [Fact]
    public void Update_profile_sets_zone_and_clears_default_flag_but_rejects_windows_ids()
    {
        var user = User.CreateEntra(Guid.NewGuid(), Guid.NewGuid(), null, Now);

        user.UpdateProfile("Ada", "Europe/Warsaw", UnitSystem.Metric);

        Assert.Equal("Europe/Warsaw", user.TimeZoneId);
        Assert.False(user.TimeZoneIsDefault);
        Assert.Throws<DomainRuleException>(() => user.UpdateProfile(null, "Central European Standard Time", UnitSystem.Metric));
        Assert.Throws<DomainRuleException>(() => user.UpdateProfile(new string('x', 101), "Europe/Warsaw", UnitSystem.Metric));
    }
}
