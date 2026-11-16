namespace RunnersDiary.Domain.Goals;

/// <summary>Lifecycle of a goal. Leaving <see cref="Active"/> removes the goal's suggestions (ADR 0039 §1). Values are stored.</summary>
public enum GoalStatus : byte
{
    Active = 0,
    Achieved = 1,
    Archived = 2,
}
