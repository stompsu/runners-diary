namespace RunnersDiary.Domain.Goals;

/// <summary>Codes of the realistic-target warnings (ADR 0039 §4). They never block saving a goal.</summary>
public enum EstimateWarningCode : byte
{
    NoRecentEffort = 0,
    TargetMuchFasterThanPredicted = 1,
    TooLittleTime = 2,
    LowWeeklyVolume = 3,
    BeyondPlanHorizon = 4,
}

public static class EstimateWarningCodes
{
    /// <summary>The lowerCamel wire code (like the <c>invalid-gpx</c> codes).</summary>
    public static string ToWire(this EstimateWarningCode code) => code switch
    {
        EstimateWarningCode.NoRecentEffort => "noRecentEffort",
        EstimateWarningCode.TargetMuchFasterThanPredicted => "targetMuchFasterThanPredicted",
        EstimateWarningCode.TooLittleTime => "tooLittleTime",
        EstimateWarningCode.LowWeeklyVolume => "lowWeeklyVolume",
        EstimateWarningCode.BeyondPlanHorizon => "beyondPlanHorizon",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown warning code."),
    };
}

public sealed record EstimateWarning(EstimateWarningCode Code, double? Value);

/// <summary>The result of <see cref="GoalEstimator.Estimate"/>: nothing is stored.</summary>
public sealed record GoalEstimate(
    DateOnly Today,
    int DaysToGoal,
    int WeeksToGoal,
    GoalTerm Term,
    int MinimumWeeks,
    double RecentWeeklyDistanceMeters,
    TimePrediction? Prediction,
    IReadOnlyList<EstimateWarning> Warnings);
