namespace RunnersDiary.Domain.Races;

/// <summary>What the caller chose when signing up (<c>SignUpForRaceRequest</c>); every member is optional.</summary>
public sealed record RaceSignupChoice(double? DistanceMeters = null, TimeOnly? StartTime = null, short? RunTypeId = null, int? TargetTimeSeconds = null)
{
    public static RaceSignupChoice None { get; } = new();
}
