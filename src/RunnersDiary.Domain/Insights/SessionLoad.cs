using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Insights;

/// <summary>
/// The sRPE load of one session (Foster 2001) as an exact integer in RPE·seconds; divide by 60 for AU only at the output boundary.
/// </summary>
/// <param name="RpeSeconds">RPE × duration in seconds; 0 when the duration is missing.</param>
/// <param name="Estimated">True when the RPE was missing and the run type's default was used.</param>
/// <param name="MissingDuration">True when the session has no duration (so no load).</param>
public readonly record struct SessionLoad(long RpeSeconds, bool Estimated, bool MissingDuration)
{
    public double Au => Rounding.Au(RpeSeconds);

    /// <summary>The load of a session, or null when its run type does not count toward load (rest) or is unknown.</summary>
    public static SessionLoad? Of(RunFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        return RunTypeCatalog.Find(fact.RunTypeId) is { CountsTowardLoad: true } type ? Of(fact, type) : null;
    }

    internal static SessionLoad Of(RunFact fact, RunType type)
    {
        if (fact.DurationSeconds is not > 0)
        {
            return new SessionLoad(0, Estimated: false, MissingDuration: true);
        }

        var estimated = fact.Rpe is null;
        var rpe = fact.Rpe ?? DefaultRpe.For(type);
        return new SessionLoad((long)rpe * fact.DurationSeconds.Value, estimated, MissingDuration: false);
    }
}
