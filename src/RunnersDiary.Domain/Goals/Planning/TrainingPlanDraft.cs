namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>
/// The model's answer as parsed (ADR 0040 §4): the strict schema's 2 + 9 members, codes still as strings. Nothing here is trusted until
/// <see cref="TrainingPlanValidator"/> has turned it into <see cref="PlannedSession"/>s.
/// </summary>
public sealed record TrainingPlanDraft(string Notes, IReadOnlyList<DraftSession> Sessions);

/// <param name="Date">Expected <c>yyyy-MM-dd</c>.</param>
/// <param name="RunType">A run type code of <see cref="PlanEnvelopeRules.AllowedRunTypeCodes"/>.</param>
/// <param name="Intensity">A <see cref="TrainingIntensities.ToWire"/> code.</param>
/// <param name="Phase">A <see cref="TrainingPhases.ToWire"/> code.</param>
/// <param name="PaceMinSecondsPerKm">The faster bound.</param>
public sealed record DraftSession(
    string Date,
    string RunType,
    string Intensity,
    string Phase,
    double? DistanceMeters,
    int? DurationSeconds,
    int? PaceMinSecondsPerKm,
    int? PaceMaxSecondsPerKm,
    string Description);
