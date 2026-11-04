using RunnersDiary.Domain.Time;

namespace RunnersDiary.Domain.Gpx;

/// <summary>A track start expressed in the run's zone; <see cref="Time"/> has minute precision (HH:mm).</summary>
public sealed record TrackStartLocalValue(DateOnly Date, TimeOnly Time, string TimeZoneId);

public static class TrackStartLocal
{
    /// <summary>Converts the GPX UTC start into the run's IANA zone (ADR 0008). Null when untimed or the zone id is invalid.</summary>
    public static TrackStartLocalValue? For(DateTime? startUtc, string timeZoneId)
    {
        if (startUtc is null || !IanaTimeZone.TryGet(timeZoneId, out var zone))
        {
            return null;
        }

        var utc = DateTime.SpecifyKind(startUtc.Value, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        return new TrackStartLocalValue(DateOnly.FromDateTime(local), new TimeOnly(local.Hour, local.Minute), timeZoneId);
    }
}
