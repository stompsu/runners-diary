namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>One session of a plan as the validator keeps it (ADR 0040 §5); the input of <see cref="TrainingSuggestion.Create"/>.</summary>
public sealed record PlannedSession(
    DateOnly Date,
    short RunTypeId,
    double? DistanceMeters,
    int? DurationSeconds,
    TrainingIntensity Intensity,
    int? TargetPaceMinSecondsPerKm,
    int? TargetPaceMaxSecondsPerKm,
    string Description,
    TrainingPhase Phase);
