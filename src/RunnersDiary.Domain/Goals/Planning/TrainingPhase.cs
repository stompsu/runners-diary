namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>The phase of a plan week (ADR 0040 §2). Values are stored.</summary>
public enum TrainingPhase : byte
{
    Base = 0,
    Build = 1,
    Peak = 2,
    Taper = 3,
}

public static class TrainingPhases
{
    /// <summary>The lowerCamel code of the model schema and the fact sheet.</summary>
    public static string ToWire(this TrainingPhase phase) => phase switch
    {
        TrainingPhase.Base => "base",
        TrainingPhase.Build => "build",
        TrainingPhase.Peak => "peak",
        TrainingPhase.Taper => "taper",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown phase."),
    };

    public static bool TryFromWire(string? code, out TrainingPhase phase)
    {
        foreach (var candidate in Enum.GetValues<TrainingPhase>())
        {
            if (string.Equals(candidate.ToWire(), code, StringComparison.Ordinal))
            {
                phase = candidate;
                return true;
            }
        }

        phase = default;
        return false;
    }
}
