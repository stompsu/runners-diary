namespace RunnersDiary.Domain.Races;

/// <summary>Kind of a catalogue race (ADR 0036 §1). Values are contracts (stored as <c>tinyint</c>, bit positions of <see cref="RaceKinds"/>).</summary>
public enum RaceKind : byte
{
    Road = 0,
    Trail = 1,
    Mountain = 2,
    Ultra = 3,
    CrossCountry = 4,
    Ocr = 5,
    Other = 6,
}
