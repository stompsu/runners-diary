namespace RunnersDiary.Domain.Races;

/// <summary>The state of a proposal for the caller. <see cref="SignedUp"/> is derived from a linked run and never stored.</summary>
public enum ProposalState : byte
{
    None = 0,
    Interested = 1,
    Dismissed = 2,
    SignedUp = 3,
}

/// <summary>The caller's run that was created from a catalogue race (<c>Runs.CatalogRaceId</c>).</summary>
public sealed record LinkedRun(Guid RunId, DateOnly Date);

/// <summary>One proposal as <see cref="RaceProposalBuilder"/> returns it, already in display order.</summary>
public sealed record RaceProposal(RaceSnapshot Race, ProposalState State, Guid? LinkedRunId, bool IsNew, bool DateChanged);
