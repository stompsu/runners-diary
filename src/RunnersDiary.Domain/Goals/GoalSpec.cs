using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Goals;

/// <summary>The planning fields of a goal: the input of the estimate, the envelope and the fact sheet (no title, event name, notes or location).</summary>
public sealed record GoalSpec(
    GoalType Type,
    double DistanceMeters,
    double? ElevationGainMeters,
    DateOnly TargetDate,
    int? TargetDurationSeconds,
    Surface? Surface)
{
    /// <summary>The target pace in whole seconds per km, or null without a target time.</summary>
    public int? TargetPaceSecondsPerKm =>
        TargetDurationSeconds is { } seconds && DistanceMeters > 0
            ? (int)Math.Round(seconds / (DistanceMeters / 1000), MidpointRounding.AwayFromZero)
            : null;
}
