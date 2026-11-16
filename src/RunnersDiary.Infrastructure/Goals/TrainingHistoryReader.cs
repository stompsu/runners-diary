using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Goals;

/// <summary>
/// Reads the inputs of the goal estimate and (M9 steps 4-5) of the plan: no-tracking projections of the caller's completed runs, never notes,
/// titles, locations or tracks. The <c>Owner</c> filter scopes every query.
/// </summary>
public sealed class TrainingHistoryReader(RunnersDiaryDbContext db)
{
    private static readonly short[] RaceTypeIds = [.. RunTypeCatalog.All.Where(t => t.Category == RunTypeCategory.Race).Select(t => t.Id)];

    /// <summary>Completed runs of the last 112 days up to <paramref name="today"/> (the estimate's and the history's input).</summary>
    public async Task<List<RunFact>> ReadFactsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var from = today.AddDays(-(GoalEstimator.TrainingLookbackDays - 1));
        return await db.Runs.AsNoTracking()
            .Where(r => r.Status == RunStatus.Completed && r.Date >= from && r.Date <= today)
            .Select(r => new RunFact(r.Id, r.Date, r.Status, r.RunTypeId, r.DistanceMeters, r.DurationSeconds, r.ElevationGainMeters, r.Rpe, r.Surface, r.AvgHeartRate))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Completed race-category runs of the last 365 days up to <paramref name="today"/> with their race details.</summary>
    public async Task<List<RaceFact>> ReadRacesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var from = today.AddDays(-(GoalEstimator.RaceLookbackDays - 1));
        var rows = await db.Runs.AsNoTracking()
            .Where(r => r.Status == RunStatus.Completed && RaceTypeIds.Contains(r.RunTypeId) && r.Race != null && r.Date >= from && r.Date <= today)
            .Select(r => new
            {
                r.Id,
                r.Date,
                r.Status,
                r.RunTypeId,
                r.DistanceMeters,
                r.DurationSeconds,
                r.Race!.Name,
                r.Race.DistanceCategory,
                r.Race.OfficialDistanceMeters,
                r.Race.ResultTimeSeconds,
                r.Race.TargetTimeSeconds,
                r.Race.OverallPlace,
                r.Race.CategoryPlace,
                r.Race.CategoryName,
                Stars = r.Rating != null ? (int?)r.Rating.Stars : null,
            })
            .ToListAsync(cancellationToken);
        return
        [
            .. rows.Select(r => new RaceFact(
                r.Id, r.Date, r.Status, r.RunTypeId, r.Name, r.DistanceCategory, r.OfficialDistanceMeters, r.DistanceMeters, r.DurationSeconds,
                r.ResultTimeSeconds, r.TargetTimeSeconds, r.OverallPlace, r.CategoryPlace, r.CategoryName, r.Stars)),
        ];
    }
}
