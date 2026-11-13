namespace RunnersDiary.Domain.Races;

/// <summary>Bit set of <see cref="RaceKind"/> (bit = 1 &lt;&lt; kind value), stored in <c>RaceInterestProfiles.Kinds</c>.</summary>
[Flags]
public enum RaceKinds : byte
{
    None = 0,
    Road = 1 << (int)RaceKind.Road,
    Trail = 1 << (int)RaceKind.Trail,
    Mountain = 1 << (int)RaceKind.Mountain,
    Ultra = 1 << (int)RaceKind.Ultra,
    CrossCountry = 1 << (int)RaceKind.CrossCountry,
    Ocr = 1 << (int)RaceKind.Ocr,
    Other = 1 << (int)RaceKind.Other,
}

public static class RaceKindsExtensions
{
    public const RaceKinds All = (RaceKinds)0b111_1111;

    public static RaceKinds ToSet(this RaceKind kind) =>
        Enum.IsDefined(kind) ? (RaceKinds)(1 << (int)kind) : RaceKinds.None;

    public static RaceKinds ToSet(this IEnumerable<RaceKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var set = RaceKinds.None;
        foreach (var kind in kinds)
        {
            set |= kind.ToSet();
        }

        return set;
    }

    public static bool Contains(this RaceKinds set, RaceKind kind) => (set & kind.ToSet()) != RaceKinds.None && Enum.IsDefined(kind);

    public static IReadOnlyList<RaceKind> ToKinds(this RaceKinds set) =>
        Enum.GetValues<RaceKind>().Where(k => set.Contains(k)).ToList();
}
