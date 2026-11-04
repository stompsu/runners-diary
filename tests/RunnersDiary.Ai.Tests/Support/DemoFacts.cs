using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>The real <c>seed/demo/runs.json</c> as a guest would have it on a given "today" (the M5 DemoInsightsTests pattern).</summary>
internal static class DemoFacts
{
    private static readonly DemoTemplate Template = DemoTemplateParser.Parse(File.ReadAllText(Path.Combine(RepoPaths.DemoSeed, "runs.json")));

    public static TheoryData<DateOnly> EveryDayOf2028()
    {
        var data = new TheoryData<DateOnly>();
        for (var day = new DateOnly(2028, 1, 1); day.Year == 2028; day = day.AddDays(1))
        {
            data.Add(day);
        }

        return data;
    }

    public static (List<RunFact> Facts, List<RaceFact> Races) Facts(DateOnly today)
    {
        var facts = new List<RunFact>();
        var races = new List<RaceFact>();
        var index = 0;
        foreach (var run in DemoScheduleExpander.Expand(Template, today))
        {
            var v = run.Values;
            var id = new Guid(++index, 0, 0, new byte[8]);
            facts.Add(new RunFact(id, v.Date, v.Status, v.RunTypeId, v.DistanceMeters, v.DurationSeconds, v.ElevationGainMeters, v.Rpe, v.Surface, v.AvgHeartRate));
            if (v.Race is { } race)
            {
                races.Add(new RaceFact(
                    id, v.Date, v.Status, v.RunTypeId, race.Name, race.DistanceCategory, race.OfficialDistanceMeters, v.DistanceMeters,
                    v.DurationSeconds, race.ResultTimeSeconds, race.TargetTimeSeconds, race.OverallPlace, race.CategoryPlace, race.CategoryName, v.Rating?.Stars));
            }
        }

        return (facts, races);
    }

    /// <summary>The dashboard default: last 12 weeks vs the previous 12 (<see cref="DemoPresets.TwelveWeeksPrevious"/>).</summary>
    public static InsightsReport TwelveWeeks(DateOnly today)
    {
        var (facts, races) = Facts(today);
        var (from, to, compare) = DemoPresets.Range(DemoPresets.TwelveWeeksPrevious, today)!.Value;
        return InsightsReportBuilder.Build(new InsightsPeriod(from, to), compare, today, facts, facts, races, null);
    }

    public static InsightsReport Report(DateOnly from, DateOnly to, ComparisonKind compare, DateOnly today)
    {
        var (facts, races) = Facts(today);
        _ = IsoWeek.MondayOf(today);
        return InsightsReportBuilder.Build(new InsightsPeriod(from, to), compare, today, facts, facts, races, null);
    }
}
