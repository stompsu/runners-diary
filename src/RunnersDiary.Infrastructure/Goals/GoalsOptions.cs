using RunnersDiary.Domain.Goals;

namespace RunnersDiary.Infrastructure.Goals;

/// <summary>Training goals and suggestions (<c>Goals</c> section, M9 plan §4.7).</summary>
public sealed class GoalsOptions
{
    public const string SectionName = "Goals";

    /// <summary>
    /// Master switch of the feature (surfaces as <c>client-config.goals.enabled</c>): while false, every <c>/api/goals</c> and
    /// <c>/api/training-suggestions</c> route answers 503 <c>feature-unavailable</c>. Off by default, on in the AppHost and tests.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Goals one user account may hold; beyond it 409 <c>goal-limit</c>.</summary>
    public int MaxGoalsPerUser { get; set; } = GoalLimits.DefaultMaxPerUser;

    /// <summary>The same cap for a guest account.</summary>
    public int MaxGoalsPerGuest { get; set; } = GoalLimits.DefaultMaxPerGuest;
}
