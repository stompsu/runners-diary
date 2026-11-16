namespace RunnersDiary.Domain.Goals;

/// <summary>Default per-account goal caps (<c>Goals:MaxGoalsPerUser</c> / <c>MaxGoalsPerGuest</c> override them).</summary>
public static class GoalLimits
{
    public const int DefaultMaxPerUser = 50;
    public const int DefaultMaxPerGuest = 10;
}
