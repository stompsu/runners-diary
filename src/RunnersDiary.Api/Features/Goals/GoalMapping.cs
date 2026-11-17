using RunnersDiary.Api.Features.Runs;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Infrastructure.Goals;

namespace RunnersDiary.Api.Features.Goals;

internal static class GoalMapping
{
    public static GoalDto ToDto(GoalView view)
    {
        var g = view.Goal;
        return new GoalDto
        {
            Id = g.Id,
            Type = g.Type,
            Title = g.Title,
            TargetDate = g.TargetDate,
            DistanceMeters = g.DistanceMeters,
            ElevationGainMeters = g.ElevationGainMeters,
            TargetDurationSeconds = g.TargetDurationSeconds,
            Surface = g.Surface,
            EventName = g.EventName,
            Location = g.Location is { } l ? new LocationDto { Latitude = l.Point.Latitude, Longitude = l.Point.Longitude, Name = l.Name } : null,
            RaceRunId = g.RaceRunId,
            Notes = g.Notes,
            Status = g.Status,
            Term = view.Term,
            RaceRunDate = view.RaceRunDate,
            Plan = g.HasPlan
                ? new GoalPlanDto
                {
                    Source = g.IsDemoPlan ? GoalPlanSource.Demo : GoalPlanSource.Model,
                    GeneratedAt = g.PlanGeneratedAt!.Value,
                    GenerationId = g.PlanGenerationId ?? Guid.Empty,
                    Notes = g.PlanNotes,
                    PromptVersion = g.PlanPromptVersion,
                    Model = g.IsDemoPlan ? null : g.PlanModelId ?? g.PlanModelDeployment,
                    HorizonEnd = g.PlanHorizonEnd ?? g.TargetDate,
                    Language = RunnersDiary.Domain.Localization.SupportedLanguages.OrDefault(g.PlanLanguage),
                    HorizonCapped = g.PlanHorizonCapped,
                    Outdated = g.PlanOutdated,
                    PendingCount = view.PendingCount,
                    DismissedCount = view.DismissedCount,
                    AcceptedCount = view.AcceptedCount,
                }
                : null,
            Version = Convert.ToBase64String(g.RowVersion),
            CreatedAt = g.CreatedAt,
            UpdatedAt = g.UpdatedAt,
        };
    }

    public static GoalEstimateResponse ToResponse(GoalEstimate e) => new()
    {
        Today = e.Today,
        DaysToGoal = e.DaysToGoal,
        WeeksToGoal = e.WeeksToGoal,
        Term = e.Term,
        MinimumWeeks = e.MinimumWeeks,
        RecentWeeklyDistanceMeters = e.RecentWeeklyDistanceMeters,
        Prediction = e.Prediction is { } p
            ? new EstimatePredictionDto
            {
                DurationSeconds = p.DurationSeconds,
                PaceSecondsPerKm = p.PaceSecondsPerKm,
                Confidence = p.Confidence,
                Basis = new EstimateBasisDto
                {
                    RunId = p.Basis.RunId,
                    Date = p.Basis.Date,
                    Kind = p.Basis.Kind,
                    DistanceMeters = p.Basis.DistanceMeters,
                    DurationSeconds = p.Basis.DurationSeconds,
                    ElevationGainMeters = p.Basis.ElevationGainMeters,
                },
            }
            : null,
        Warnings = [.. e.Warnings.Select(w => new EstimateWarningDto { Code = w.Code.ToWire(), Value = w.Value })],
    };

    /// <summary>Domain errors as the validation-problem dictionary (camelCase paths, messages grouped per path).</summary>
    public static Dictionary<string, string[]> ToErrors(IEnumerable<Domain.Common.DomainError> errors, string prefix = "") =>
        errors.GroupBy(e => prefix + e.Path).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
}
