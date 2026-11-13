using System.Diagnostics.CodeAnalysis;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Races;

/// <summary>Turns a catalogue race into the values of a planned race run (ADR 0036 §5). The result always passes <see cref="RunRules"/>.</summary>
public static class RaceRunMapper
{
    public const short RoadRaceTypeId = 1;
    public const short TrailRaceTypeId = 2;
    public const short UltraTypeId = 3;
    public const short CrossCountryTypeId = 5;
    public const int RaceNameMaxLength = 150;
    public const int LocationNameMaxLength = 200;
    private const double CategoryTolerance = 0.01;
    private const double DistanceMatchMeters = 0.5;

    /// <summary>Throws <see cref="DomainRuleException"/> with camelCase paths (<c>raceId</c>, <c>distanceMeters</c>, <c>runTypeId</c>, ...) when the signup is not valid.</summary>
    /// <param name="today">The caller's today (a race dated before it is rejected). Not part of the plan's sketch; it is needed to reject past races.</param>
    public static RunValues ToPlannedRun(RaceSnapshot race, RaceSignupChoice choice, string profileTimeZoneId, DateOnly today) =>
        TryToPlannedRun(race, choice, profileTimeZoneId, today, out var values, out var errors)
            ? values
            : throw new DomainRuleException(errors);

    public static bool TryToPlannedRun(
        RaceSnapshot race,
        RaceSignupChoice choice,
        string profileTimeZoneId,
        DateOnly today,
        [NotNullWhen(true)] out RunValues? values,
        out IReadOnlyList<DomainError> errors)
    {
        ArgumentNullException.ThrowIfNull(race);
        ArgumentNullException.ThrowIfNull(choice);
        var list = new List<DomainError>();
        values = null;
        errors = list;

        if (race.Date < today)
        {
            list.Add(new("raceId", "The race date has passed."));
        }

        var distance = ChooseDistance(race, choice, list);
        var runTypeId = ChooseRunType(race, choice, distance, list);
        if (list.Count > 0)
        {
            return false;
        }

        var candidate = new RunValues
        {
            Date = race.Date,
            StartTime = choice.StartTime ?? race.StartTime,
            TimeZoneId = race.TimeZoneId ?? profileTimeZoneId,
            Status = RunStatus.Planned,
            RunTypeId = runTypeId,
            DistanceMeters = distance,
            ElevationGainMeters = race.ElevationGainMeters,
            Surface = SurfaceFor(race),
            Location = race.Point is { } point ? new RunLocation(point, Cut(race.LocationName, LocationNameMaxLength)) : null,
            Race = new RaceDetails(
                Cut(race.Name, RaceNameMaxLength) ?? string.Empty,
                CategoryFor(distance),
                OfficialDistanceMeters: distance,
                TargetTimeSeconds: choice.TargetTimeSeconds),
        };

        list.AddRange(RunRules.Validate(candidate));
        if (list.Count > 0)
        {
            return false;
        }

        values = candidate;
        return true;
    }

    /// <summary>Within ±1 % (inclusive) of 5,000, 10,000, 21,097.5 or 42,195 m; otherwise <see cref="RaceDistanceCategory.Other"/>.</summary>
    public static RaceDistanceCategory CategoryFor(double? distanceMeters)
    {
        if (distanceMeters is not { } d)
        {
            return RaceDistanceCategory.Other;
        }

        if (Near(d, 5_000))
        {
            return RaceDistanceCategory.FiveK;
        }

        if (Near(d, 10_000))
        {
            return RaceDistanceCategory.TenK;
        }

        if (Near(d, RaceNormalizer.HalfMarathonMeters))
        {
            return RaceDistanceCategory.HalfMarathon;
        }

        return Near(d, RaceNormalizer.MarathonMeters) ? RaceDistanceCategory.Marathon : RaceDistanceCategory.Other;
    }

    private static bool Near(double d, double target) => Math.Abs(d - target) <= (target * CategoryTolerance) + 1e-9;

    private static double? ChooseDistance(RaceSnapshot race, RaceSignupChoice choice, List<DomainError> errors)
    {
        var offered = race.DistancesMeters;
        if (offered.Count == 0)
        {
            if (choice.DistanceMeters is not null)
            {
                errors.Add(new("distanceMeters", "This race lists no distances."));
            }

            return null;
        }

        if (choice.DistanceMeters is not { } wanted)
        {
            if (offered.Count == 1)
            {
                return offered[0];
            }

            errors.Add(new("distanceMeters", "Choose one of the race's distances."));
            return null;
        }

        foreach (var d in offered)
        {
            if (Math.Abs(d - wanted) < DistanceMatchMeters)
            {
                return d;
            }
        }

        errors.Add(new("distanceMeters", "Must be one of the distances the race offers."));
        return null;
    }

    private static short ChooseRunType(RaceSnapshot race, RaceSignupChoice choice, double? distance, List<DomainError> errors)
    {
        if (choice.RunTypeId is { } requested)
        {
            if (RunTypeCatalog.Find(requested) is not { Category: RunTypeCategory.Race })
            {
                errors.Add(new("runTypeId", "Must be a race run type."));
            }

            return requested;
        }

        if (race.Kind == RaceKind.Ultra || distance > UltraDistance)
        {
            return UltraTypeId;
        }

        return race.Kind switch
        {
            RaceKind.Trail or RaceKind.Mountain or RaceKind.Ocr => TrailRaceTypeId,
            RaceKind.CrossCountry => CrossCountryTypeId,
            _ => RoadRaceTypeId,
        };
    }

    private const double UltraDistance = RaceNormalizer.MarathonMeters;

    private static Surface? SurfaceFor(RaceSnapshot race) => race.Kind switch
    {
        RaceKind.Road => Surface.Road,
        RaceKind.Trail or RaceKind.Mountain or RaceKind.CrossCountry or RaceKind.Ocr => Surface.Trail,
        _ => race.Surface,
    };

    private static string? Cut(string? value, int max)
    {
        if (value is null || value.Length <= max)
        {
            return value;
        }

        var length = char.IsHighSurrogate(value[max - 1]) ? max - 1 : max;
        return value[..length];
    }
}
