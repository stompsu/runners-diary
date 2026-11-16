namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>There is no "Accepted": accepting turns the row into a run and deletes it (ADR 0041 §1). Values are stored.</summary>
public enum SuggestionStatus : byte
{
    Pending = 0,
    Dismissed = 1,
}
