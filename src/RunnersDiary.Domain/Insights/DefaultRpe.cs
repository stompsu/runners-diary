using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Insights;

/// <summary>Per-type default RPE used when a session has a duration but no RPE (ADR 0024 §2, v1). Such sessions are flagged as estimated.</summary>
public static class DefaultRpe
{
    private const int Fallback = 5;

    public static int For(RunType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Code switch
        {
            "recovery" => 2,
            "easy" or "stridesDrills" or "treadmill" => 3,
            "crossTraining" => 4,
            "long" or "trailTraining" => 5,
            "fartlek" or "progression" => 6,
            "tempo" or "racePace" => 7,
            "intervals" or "hillRepeats" or "ultra" => 8,
            "roadRace" or "trailRace" or "trackRace" or "crossCountry" or "virtualRace" => 9,
            _ => Fallback,
        };
    }
}
