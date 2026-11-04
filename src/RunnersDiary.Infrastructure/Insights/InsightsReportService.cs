using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Domain.Time;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Insights;

/// <summary>
/// Loads the inputs of <see cref="InsightsReportBuilder"/> with at most five no-tracking projection queries, all index seeks on
/// <c>(UserId, Date)</c> (ADR 0025 §1). No notes, locations or tracks are read.
/// </summary>
public sealed class InsightsReportService(RunnersDiaryDbContext db, ICurrentUser currentUser, TimeProvider clock) : IInsightsReportService
{
    private static readonly short[] LoadTypeIds = [.. RunTypeCatalog.All.Where(t => t.CountsTowardLoad).Select(t => t.Id)];
    private static readonly short[] RaceTypeIds = [.. RunTypeCatalog.All.Where(t => t.Category == RunTypeCategory.Race).Select(t => t.Id)];

    public async Task<InsightsReport> GetReportAsync(DateOnly firstDay, DateOnly lastDay, ComparisonKind compare, CancellationToken cancellationToken)
    {
        var period = new InsightsPeriod(firstDay, lastDay);
        var userId = currentUser.UserId;

        // 1. The caller's zone gives "today" (ADR 0008).
        var zone = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? User.DefaultEntraTimeZoneId;
        var today = UserToday.Get(clock, zone);

        // 2. Completed and planned facts for the buckets, the load history and the planned summary.
        var factsStart = InsightsReportBuilder.RequiredFactsStart(period, today);
        var factsEnd = InsightsReportBuilder.RequiredFactsEnd(period);
        var facts = await QueryFacts(factsStart, factsEnd, completedOnly: false).ToListAsync(cancellationToken);

        // 3. The comparison window, minus the part query 2 already read.
        var comparisonFacts = new List<RunFact>();
        if (ComparisonWindow.For(period, compare, today) is { } window)
        {
            comparisonFacts.AddRange(facts.Where(f => f.Status == RunStatus.Completed && f.Date >= window.From && f.Date <= window.To));
            if (window.From < factsStart)
            {
                var missingEnd = window.To < factsStart.AddDays(-1) ? window.To : factsStart.AddDays(-1);
                comparisonFacts.AddRange(await QueryFacts(window.From, missingEnd, completedOnly: true).ToListAsync(cancellationToken));
            }
        }

        // 4. The first completed load-bearing session ever (the ACWR needs 35 days of diary history).
        var historyStart = await db.Runs.AsNoTracking()
            .Where(r => r.Status == RunStatus.Completed && r.Date <= today && LoadTypeIds.Contains(r.RunTypeId))
            .MinAsync(r => (DateOnly?)r.Date, cancellationToken);

        // 5. Race results up to the end of the period (for "PB at the time") and the planned races inside it.
        var raceRows = await db.Runs.AsNoTracking()
            .Where(r => RaceTypeIds.Contains(r.RunTypeId) && r.Date <= period.To && r.Race != null)
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
        var races = raceRows
            .Select(r => new RaceFact(
                r.Id,
                r.Date,
                r.Status,
                r.RunTypeId,
                r.Name,
                r.DistanceCategory,
                r.OfficialDistanceMeters,
                r.DistanceMeters,
                r.DurationSeconds,
                r.ResultTimeSeconds,
                r.TargetTimeSeconds,
                r.OverallPlace,
                r.CategoryPlace,
                r.CategoryName,
                r.Stars))
            .ToList();

        return InsightsReportBuilder.Build(period, compare, today, facts, comparisonFacts, races, historyStart);
    }

    /// <summary>A projection of the caller's runs in a date range (the builder sorts by (Date, Id) itself). The owner filter applies.</summary>
    private IQueryable<RunFact> QueryFacts(DateOnly from, DateOnly to, bool completedOnly) =>
        db.Runs.AsNoTracking()
            .Where(r => r.Date >= from && r.Date <= to && (!completedOnly || r.Status == RunStatus.Completed))
            .Select(r => new RunFact(
                r.Id,
                r.Date,
                r.Status,
                r.RunTypeId,
                r.DistanceMeters,
                r.DurationSeconds,
                r.ElevationGainMeters,
                r.Rpe,
                r.Surface,
                r.AvgHeartRate));
}
