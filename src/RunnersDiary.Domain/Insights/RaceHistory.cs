using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Insights;

public sealed record RaceResult(
    Guid RunId,
    DateOnly Date,
    RunStatus Status,
    string RunTypeCode,
    string Name,
    RaceDistanceCategory DistanceCategory,
    double? OfficialDistanceMeters,
    double? DistanceMeters,
    int? ResultTimeSeconds,
    int? TargetTimeSeconds,
    double? PaceSecondsPerKm,
    int? OverallPlace,
    int? CategoryPlace,
    string? CategoryName,
    int? RatingStars,
    bool IsPersonalBest);

/// <summary>Race results in a period with "PB at the time" flags (ADR 0024 §8).</summary>
public static class RaceHistory
{
    /// <param name="races">All of the user's race-category runs up to the end of the period (the PB check looks at earlier races too).</param>
    /// <param name="period">Only races inside it are listed.</param>
    /// <param name="today">Completed races dated after today are ignored.</param>
    public static IReadOnlyList<RaceResult> Compute(IEnumerable<RaceFact> races, InsightsPeriod period, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(races);

        var ordered = races
            .Where(r => r.Status == RunStatus.Planned || r.Date <= today)
            .OrderBy(r => r.Date).ThenBy(r => r.Id)
            .ToList();
        var results = ordered
            .Where(r => r.Status == RunStatus.Completed && Time(r) is > 0)
            .ToList();

        var list = new List<RaceResult>();
        foreach (var race in ordered.Where(r => r.Date >= period.From && r.Date <= period.To))
        {
            var completed = race.Status == RunStatus.Completed;
            var time = completed ? Time(race) : null;
            var distance = race.DistanceMeters ?? race.OfficialDistanceMeters;
            double? pace = time is > 0 && distance is > 0 ? Rounding.Round(time.Value / distance.Value * 1000, 1) : null;

            list.Add(new RaceResult(
                race.Id,
                race.Date,
                race.Status,
                RunTypeCatalog.Get(race.RunTypeId).Code,
                race.Name,
                race.Category,
                race.OfficialDistanceMeters,
                race.DistanceMeters,
                time,
                race.TargetTimeSeconds,
                pace,
                completed ? race.OverallPlace : null,
                completed ? race.CategoryPlace : null,
                race.CategoryName,
                completed ? race.RatingStars : null,
                completed && time is > 0 && IsPersonalBest(race, time.Value, results)));
        }

        return list;
    }

    private static int? Time(RaceFact race) => race.ResultTimeSeconds ?? race.DurationSeconds;

    /// <summary>The fastest completed result in the category on or before the date; on a tie the earlier race holds it. <c>Other</c> never has a PB.</summary>
    private static bool IsPersonalBest(RaceFact race, int time, List<RaceFact> results)
    {
        if (race.Category == RaceDistanceCategory.Other)
        {
            return false;
        }

        foreach (var other in results)
        {
            if (other.Id == race.Id || other.Category != race.Category || other.Date > race.Date)
            {
                continue;
            }

            var otherTime = Time(other)!.Value;
            var otherIsEarlier = (other.Date, other.Id).CompareTo((race.Date, race.Id)) < 0;
            if (otherTime < time || (otherTime == time && otherIsEarlier))
            {
                return false;
            }
        }

        return true;
    }
}
