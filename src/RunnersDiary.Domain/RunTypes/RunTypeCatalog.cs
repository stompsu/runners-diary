using System.Collections.Frozen;

namespace RunnersDiary.Domain.RunTypes;

/// <summary>The fixed run-type catalog (docs/data-model.md). Seeded into the database with <c>HasData</c>; changing it requires a migration.</summary>
public static class RunTypeCatalog
{
    public static IReadOnlyList<RunType> All { get; } = Build();

    private static readonly FrozenDictionary<short, RunType> ById = All.ToFrozenDictionary(t => t.Id);
    private static readonly FrozenDictionary<string, RunType> ByCode = All.ToFrozenDictionary(t => t.Code, StringComparer.Ordinal);

    public static RunType? Find(short id) => ById.GetValueOrDefault(id);

    public static RunType? FindByCode(string code) => ByCode.GetValueOrDefault(code);

    public static RunType Get(short id) =>
        Find(id) ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown run type id.");

    private static List<RunType> Build()
    {
        short order = 0;
        RunType Race(short id, string code) => new(id, code, RunTypeCategory.Race, true, true, false, ++order);
        RunType Training(short id, string code) => new(id, code, RunTypeCategory.Training, true, true, false, ++order);

        return
        [
            Race(1, "roadRace"),
            Race(2, "trailRace"),
            Race(3, "ultra"),
            Race(4, "trackRace"),
            Race(5, "crossCountry"),
            Race(6, "virtualRace"),
            Training(20, "easy"),
            Training(21, "recovery"),
            Training(22, "long"),
            Training(23, "tempo"),
            Training(24, "intervals"),
            Training(25, "fartlek"),
            Training(26, "hillRepeats"),
            Training(27, "progression"),
            Training(28, "racePace"),
            Training(29, "stridesDrills"),
            Training(30, "trailTraining"),
            Training(31, "treadmill"),
            new(40, "crossTraining", RunTypeCategory.Other, false, true, false, ++order),
            new(41, "rest", RunTypeCategory.Other, false, false, true, ++order),
        ];
    }
}
