namespace RunnersDiary.Domain.Time;

/// <summary>Derives <c>StartUtc</c> from a local date/time and an IANA zone (ADR 0008).</summary>
public static class LocalStartResolver
{
    /// <summary>
    /// Returns the UTC instant (seconds precision, <see cref="DateTimeKind.Utc"/>) or null when <paramref name="startTime"/> is null.
    /// A non-existent local time (spring forward) is shifted forward by the gap; an ambiguous one (fall back) resolves to the earlier occurrence.
    /// </summary>
    public static DateTime? ResolveUtc(DateOnly date, TimeOnly? startTime, string timeZoneId)
    {
        if (startTime is null)
        {
            return null;
        }

        var zone = IanaTimeZone.Get(timeZoneId);
        var t = startTime.Value;
        var local = DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(t.Hour, t.Minute, t.Second)), DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            var before = zone.GetUtcOffset(local.AddHours(-3));
            var after = zone.GetUtcOffset(local.AddHours(3));
            var shifted = local + (after - before);
            return ToUtc(shifted, after);
        }

        if (zone.IsAmbiguousTime(local))
        {
            // The earlier occurrence is the one with the larger (daylight) offset.
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return ToUtc(local, offset);
        }

        return ToUtc(local, zone.GetUtcOffset(local));
    }

    private static DateTime ToUtc(DateTime local, TimeSpan offset) => DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
}
