namespace RunnersDiary.Domain.Races;

/// <summary>The stored per-user marks. "None" is the absence of a row; "SignedUp" is derived from a linked run, never stored.</summary>
public enum RaceProposalMark : byte
{
    Interested = 1,
    Dismissed = 2,
}
