namespace RunnersDiary.Domain.Races;

/// <summary>Where a catalogue race comes from. Stored as <c>tinyint</c>; values are contracts. A lower value wins source-priority ties in the duplicate matcher.</summary>
public enum RaceSource : byte
{
    RunningLife = 1,
    Demo = 100,
}
