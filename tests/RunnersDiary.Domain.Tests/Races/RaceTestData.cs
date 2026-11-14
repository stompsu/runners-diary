using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Races;

internal static class RaceTestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 13, 6, 0, 0, 500, TimeSpan.Zero);

    /// <summary>A valid, normalised Demo race: Gliwice 10K on 2027-03-14.</summary>
    public static RaceFacts Facts(Func<RaceFacts, RaceFacts>? change = null)
    {
        var facts = new RaceFacts(
            RaceSource.Demo,
            "demo-1",
            "Demo Gliwice Night 10K",
            new DateOnly(2027, 3, 14),
            new TimeOnly(10, 0),
            "Gliwice",
            new GeoPoint(50.2945, 18.6714),
            "PL",
            "PL-24",
            RaceKind.Road,
            Surface.Road,
            [10_000],
            50,
            "https://example.org/demo-signup",
            null,
            null,
            60m,
            "PLN",
            new DateOnly(2027, 3, 1),
            "Klub Biegacza Gliwice");
        return change is null ? facts : change(facts);
    }

    public static RaceSnapshot Snapshot(
        string name = "Demo Gliwice Night 10K",
        DateOnly? date = null,
        RaceKind kind = RaceKind.Road,
        IReadOnlyList<double>? distances = null,
        string? regionCode = "PL-24",
        GeoPoint? point = null,
        DateTime? importedUtc = null,
        Guid? id = null,
        string? locationName = "Gliwice",
        string? timeZoneId = "Europe/Warsaw",
        TimeOnly? startTime = null,
        Surface? surface = null,
        double? elevation = null) =>
        new(
            id ?? Guid.NewGuid(),
            RaceSource.Demo,
            name,
            date ?? new DateOnly(2026, 12, 6),
            startTime,
            timeZoneId,
            locationName,
            point,
            "PL",
            regionCode,
            kind,
            surface,
            distances ?? [10_000],
            elevation,
            importedUtc ?? new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

    public static RaceInterestProfile Profile(Func<RaceInterestValues, RaceInterestValues>? change = null, DateTimeOffset? now = null)
    {
        var values = RaceInterestValues.Defaults with { MinDistanceMeters = null, MaxDistanceMeters = null, Kinds = [.. Enum.GetValues<RaceKind>()], HorizonDays = 120 };
        return RaceInterestProfile.Create(Guid.NewGuid(), change is null ? values : change(values), now ?? Now);
    }
}
