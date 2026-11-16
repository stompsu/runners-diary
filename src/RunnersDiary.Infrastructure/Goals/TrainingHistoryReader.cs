using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
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

    /// <summary>
    /// The fixed commitments of a plan for <paramref name="goalId"/> (ADR 0040 §2): every run in <c>[planStart, planEnd]</c> (planned or completed,
    /// any type; race-category runs as <see cref="CommitmentKind.Race"/>, runs accepted from this goal marked <c>FromThisGoal</c>), the planned
    /// race-category runs in <c>(planEnd, targetDate]</c>, and the target dates of the caller's other Active goals in <c>[planStart, planEnd]</c>.
    /// Dates, types, distances and durations only: no title, notes or location.
    /// </summary>
    public async Task<List<CommitmentFact>> ReadCommitmentsAsync(
        Guid goalId, DateOnly planStart, DateOnly planEnd, DateOnly targetDate, CancellationToken cancellationToken)
    {
        var runs = await db.Runs.AsNoTracking()
            .Where(r => r.Date >= planStart && r.Date <= targetDate)
            .Select(r => new { r.Date, r.RunTypeId, r.DistanceMeters, r.DurationSeconds, r.Status, r.GoalId })
            .ToListAsync(cancellationToken);
        var commitments = new List<CommitmentFact>();
        foreach (var r in runs.OrderBy(r => r.Date))
        {
            var isRace = RaceTypeIds.Contains(r.RunTypeId);
            if (r.Date <= planEnd)
            {
                commitments.Add(new CommitmentFact(
                    r.Date, r.RunTypeId, r.DistanceMeters, r.DurationSeconds, isRace ? CommitmentKind.Race : CommitmentKind.Planned, r.GoalId == goalId));
            }
            else if (isRace && r.Status == RunStatus.Planned)
            {
                commitments.Add(new CommitmentFact(r.Date, r.RunTypeId, r.DistanceMeters, r.DurationSeconds, CommitmentKind.Race, r.GoalId == goalId));
            }
        }

        var otherGoalDates = await db.Goals.AsNoTracking()
            .Where(g => g.Id != goalId && g.Status == GoalStatus.Active && g.TargetDate >= planStart && g.TargetDate <= planEnd)
            .Select(g => g.TargetDate)
            .ToListAsync(cancellationToken);
        commitments.AddRange(otherGoalDates.Distinct().Select(d => new CommitmentFact(d, null, null, null, CommitmentKind.OtherGoal, false)));
        return commitments;
    }

    /// <summary>The date and run type of each of the goal's Dismissed suggestions: "the user declined these" (ADR 0040 §3).</summary>
    public async Task<List<DismissedHint>> ReadDismissedAsync(Guid goalId, CancellationToken cancellationToken) =>
        await db.TrainingSuggestions.AsNoTracking()
            .Where(s => s.GoalId == goalId && s.Status == SuggestionStatus.Dismissed)
            .OrderBy(s => s.Date)
            .Select(s => new DismissedHint(s.Date, s.RunTypeId))
            .ToListAsync(cancellationToken);
}
