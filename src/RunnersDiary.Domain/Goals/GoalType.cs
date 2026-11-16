namespace RunnersDiary.Domain.Goals;

/// <summary>What a goal asks for (ADR 0039 §1). Values are stored.</summary>
public enum GoalType : byte
{
    /// <summary>A named event; needs an event name.</summary>
    Race = 0,

    /// <summary>A distance in a target time; needs a target duration.</summary>
    Performance = 1,

    /// <summary>Complete a distance, optionally with elevation; has no target duration.</summary>
    Distance = 2,
}
