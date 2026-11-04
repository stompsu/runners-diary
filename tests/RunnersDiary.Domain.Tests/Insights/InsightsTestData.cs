using System.Globalization;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Tests.Insights;

internal static class InsightsTestData
{
    public static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A run fact; ids are sequential so (Date, Id) ordering is predictable.</summary>
    public static RunFact Run(
        string date,
        string type = "easy",
        double? meters = null,
        int? seconds = null,
        int? rpe = null,
        RunStatus status = RunStatus.Completed,
        Surface? surface = null,
        int? hr = null,
        double? gain = null,
        int id = 0) =>
        new(new Guid(id, 0, 0, new byte[8]), D(date), status, RunTypeCatalog.FindByCode(type)!.Id, meters, seconds, gain, rpe, surface, hr);

    /// <summary>One session per day of exactly <paramref name="au"/> arbitrary units (RPE 5, so seconds = au × 12), from <paramref name="start"/> for <paramref name="days"/>.</summary>
    public static IEnumerable<RunFact> Daily(DateOnly start, int days, int au, string type = "easy")
    {
        for (var i = 0; i < days; i++)
        {
            yield return new RunFact(
                new Guid(i, 0, 0, new byte[8]),
                start.AddDays(i),
                RunStatus.Completed,
                RunTypeCatalog.FindByCode(type)!.Id,
                null,
                au * 12,
                null,
                5,
                null,
                null);
        }
    }

    public static InsightsReport Build(
        string from,
        string to,
        string today,
        IEnumerable<RunFact> facts,
        ComparisonKind compare = ComparisonKind.None,
        IEnumerable<RaceFact>? races = null,
        DateOnly? historyStart = null)
    {
        var list = facts.ToList();
        return InsightsReportBuilder.Build(new InsightsPeriod(D(from), D(to)), compare, D(today), list, list, [.. races ?? []], historyStart);
    }

    public static RaceFact Race(
        string date,
        RaceDistanceCategory category,
        int? result,
        int id,
        RunStatus status = RunStatus.Completed,
        int? target = null,
        string name = "Race",
        double? distance = null) =>
        new(new Guid(id, 0, 0, new byte[8]), D(date), status, 1, name, category, null, distance, result, result, target, null, null, null, null);
}
