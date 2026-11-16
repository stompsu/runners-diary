namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>How hard a suggested session is. <see cref="Rest"/> only goes with the rest run type. Values are stored.</summary>
public enum TrainingIntensity : byte
{
    Recovery = 0,
    Easy = 1,
    Steady = 2,
    Tempo = 3,
    Threshold = 4,
    Interval = 5,
    RacePace = 6,
    Rest = 7,
}

public static class TrainingIntensities
{
    /// <summary>Hard intensities for the hard-session rules of ADR 0040 §5.</summary>
    public static bool IsHard(this TrainingIntensity intensity) =>
        intensity is TrainingIntensity.Tempo or TrainingIntensity.Threshold or TrainingIntensity.Interval or TrainingIntensity.RacePace;

    /// <summary>The lowerCamel code of the model schema (ADR 0040 §4).</summary>
    public static string ToWire(this TrainingIntensity intensity) => intensity switch
    {
        TrainingIntensity.Recovery => "recovery",
        TrainingIntensity.Easy => "easy",
        TrainingIntensity.Steady => "steady",
        TrainingIntensity.Tempo => "tempo",
        TrainingIntensity.Threshold => "threshold",
        TrainingIntensity.Interval => "interval",
        TrainingIntensity.RacePace => "racePace",
        TrainingIntensity.Rest => "rest",
        _ => throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "Unknown intensity."),
    };

    public static bool TryFromWire(string? code, out TrainingIntensity intensity)
    {
        foreach (var candidate in Enum.GetValues<TrainingIntensity>())
        {
            if (string.Equals(candidate.ToWire(), code, StringComparison.Ordinal))
            {
                intensity = candidate;
                return true;
            }
        }

        intensity = default;
        return false;
    }
}
