using RunnersDiary.Domain.Goals;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Goals;

/// <summary>The realistic-target estimate (ADR 0039 §4): reads the caller's recent runs and calls the pure estimator. No side effects.</summary>
public sealed class GoalEstimateService(
    RunnersDiaryDbContext db, ICurrentUser currentUser, TimeProvider clock, TrainingHistoryReader reader)
{
    /// <summary>The estimate, or the 400 errors of <see cref="GoalRules.ValidateEstimate"/>.</summary>
    public async Task<(GoalEstimate? Estimate, IReadOnlyList<Domain.Common.DomainError> Errors)> EstimateAsync(
        GoalSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var (_, today) = await CallerClock.GetAsync(db, currentUser, clock, cancellationToken);

        var errors = GoalRules.ValidateEstimate(spec, today);
        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var facts = await reader.ReadFactsAsync(today, cancellationToken);
        var races = await reader.ReadRacesAsync(today, cancellationToken);
        return (GoalEstimator.Estimate(spec, today, facts, races), []);
    }
}
