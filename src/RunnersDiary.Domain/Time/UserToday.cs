namespace RunnersDiary.Domain.Time;

/// <summary>"Today" in the user's zone, not the server's (ADR 0008).</summary>
public static class UserToday
{
    public static DateOnly Get(TimeProvider timeProvider, string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), IanaTimeZone.Get(timeZoneId));
        return DateOnly.FromDateTime(local.DateTime);
    }
}
