using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Tests.Insights;

namespace RunnersDiary.Domain.Tests.Goals;

public sealed class TrainingHistoryTests
{
    // A Monday: the ten complete weeks start 2026-09-07 .. 2026-11-09.
    internal static readonly DateOnly Today = new(2026, 11, 16);

    internal static List<RunFact> Facts() =>
    [
        InsightsTestData.Run("2026-10-20", "easy", 5_000, 1_500, rpe: 3, id: 1),
        InsightsTestData.Run("2026-10-27", "easy", 10_000, 3_000, rpe: 7, id: 2), // hard by RPE
        InsightsTestData.Run("2026-11-03", "easy", 10_000, 3_000, rpe: 3, id: 3),
        InsightsTestData.Run("2026-11-07", "roadRace", 10_000, 2_900, rpe: 9, id: 4), // hard: a race
        InsightsTestData.Run("2026-11-10", "easy", 10_000, 3_000, rpe: 3, id: 5),
        InsightsTestData.Run("2026-11-12", "tempo", 8_000, 2_400, rpe: 6, id: 6), // hard by type
        InsightsTestData.Run("2026-11-15", "long", 20_000, 7_200, rpe: 5, id: 7),
        InsightsTestData.Run("2026-11-11", "crossTraining", 0, 3_600, id: 8), // not running
        InsightsTestData.Run("2026-11-13", "easy", 50_000, 3_600, status: RunStatus.Planned, id: 9), // planned
        InsightsTestData.Run("2026-11-16", "easy", 7_000, 2_000, id: 10), // this week: not a complete week
    ];

    [Fact]
    public void Ten_complete_weeks_oldest_first_with_hand_computed_figures()
    {
        var h = TrainingHistory.From(Facts(), Today);

        Assert.Equal(10, h.Weeks.Count);
        Assert.Equal(new DateOnly(2026, 9, 7), h.Weeks[0].Start);
        Assert.Equal(new DateOnly(2026, 11, 9), h.Weeks[9].Start);
        Assert.All(h.Weeks.Take(6), w => Assert.Equal(0, w.Sessions));

        Assert.Equal(new HistoryWeek(new DateOnly(2026, 10, 19), 5_000, 1_500, 1, 5_000, 0), h.Weeks[6]);
        Assert.Equal(new HistoryWeek(new DateOnly(2026, 10, 26), 10_000, 3_000, 1, 10_000, 1), h.Weeks[7]);
        Assert.Equal(new HistoryWeek(new DateOnly(2026, 11, 2), 20_000, 5_900, 2, 10_000, 1), h.Weeks[8]);
        Assert.Equal(new HistoryWeek(new DateOnly(2026, 11, 9), 38_000, 12_600, 3, 20_000, 1), h.Weeks[9]);
    }

    [Fact]
    public void Baseline_figures_cover_the_last_four_complete_weeks()
    {
        var h = TrainingHistory.From(Facts(), Today);

        Assert.Equal(18_250, h.MeanWeeklyDistanceMeters); // (5000 + 10000 + 20000 + 38000) / 4
        Assert.Equal(20_000, h.LongestRunMeters);
        Assert.Equal(0.7, h.SessionsPerWeek); // 7 sessions in 10 weeks
        Assert.Equal(315.1, h.MeanPaceSecondsPerKm); // 23,000 s over 73 km
    }

    [Fact]
    public void A_weekday_is_usual_when_it_has_a_run_in_at_least_40_percent_of_the_weeks()
    {
        // Tuesday: weeks of 10-19, 10-26, 11-02, 11-09 = 4 of 10. Thursday, Saturday, Sunday: 1 each.
        Assert.Equal([DayOfWeek.Tuesday], TrainingHistory.From(Facts(), Today).UsualWeekdays);

        var three = Facts().Where(f => f.Id != new Guid(1, 0, 0, new byte[8])).ToList(); // Tuesday in 3 of 10 weeks
        Assert.Empty(TrainingHistory.From(three, Today).UsualWeekdays);
    }

    [Fact]
    public void Running_sessions_of_the_last_84_days_include_today_and_exclude_day_85()
    {
        var facts = Facts();
        Assert.Equal(7 + 1, TrainingHistory.From(facts, Today).RunningSessionsLast84Days); // seven complete-week sessions + today's run

        facts.Add(InsightsTestData.Run("2026-08-25", "easy", 5_000, 1_500, id: 20)); // 83 days ago: inside
        facts.Add(InsightsTestData.Run("2026-08-24", "easy", 5_000, 1_500, id: 21)); // 84 days ago: outside
        Assert.Equal(9, TrainingHistory.From(facts, Today).RunningSessionsLast84Days);
    }

    [Fact]
    public void No_running_gives_zero_figures_and_no_pace()
    {
        var h = TrainingHistory.From([], Today);

        Assert.Equal(10, h.Weeks.Count);
        Assert.Equal(0, h.MeanWeeklyDistanceMeters);
        Assert.Equal(0, h.LongestRunMeters);
        Assert.Null(h.MeanPaceSecondsPerKm);
        Assert.Empty(h.UsualWeekdays);
        Assert.Equal(0, h.RunningSessionsLast84Days);
    }

    [Fact]
    public void On_a_sunday_the_current_week_is_still_incomplete()
    {
        var sunday = new DateOnly(2026, 11, 22);
        var h = TrainingHistory.From([InsightsTestData.Run("2026-11-22", "easy", 5_000, 1_500, id: 1)], sunday);

        Assert.Equal(new DateOnly(2026, 11, 9), h.Weeks[^1].Start);
        Assert.Equal(0, h.Weeks[^1].Sessions);
    }

    // Today 2026-11-16: the base window is 2026-10-19 .. 2026-11-15.
    private static RunFact Easy(string date, int id, double meters = 8_000, int seconds = 2_880, string type = "easy", Surface? surface = null) =>
        InsightsTestData.Run(date, type, meters, seconds, surface: surface, id: id);

    [Fact]
    public void Recent_easy_pace_is_the_median_of_easy_recovery_and_long_runs_of_the_base_window()
    {
        var facts = new List<RunFact>
        {
            Easy("2026-10-20", 1, seconds: 2_800),                 // 350
            Easy("2026-10-27", 2, seconds: 2_880),                 // 360
            Easy("2026-11-03", 3, seconds: 3_200, type: "long"),   // 400
            Easy("2026-11-05", 4, seconds: 2_160, type: "tempo"),  // not an easy type
            Easy("2026-11-06", 5, seconds: 1_000),                 // 125 s/km: out of range
            Easy("2026-10-12", 6, seconds: 5_000),                 // before the window
            Easy("2026-11-16", 7, seconds: 5_000),                 // this week
        };

        Assert.Equal(360, TrainingHistory.From(facts, Today).RecentEasyPaceSecondsPerKm);
    }

    [Fact]
    public void Recent_easy_pace_excludes_trail_and_treadmill_and_runs_under_one_km()
    {
        var facts = new List<RunFact>
        {
            Easy("2026-10-20", 1, seconds: 2_800),
            Easy("2026-10-27", 2, seconds: 2_880),
            Easy("2026-11-03", 3, seconds: 3_200, surface: Surface.Trail),
            Easy("2026-11-04", 4, seconds: 3_200, surface: Surface.Treadmill),
            Easy("2026-11-05", 5, meters: 900, seconds: 600),
        };

        var h = TrainingHistory.From(facts, Today);

        // Two usable runs only: falls back to the mean pace of the window.
        Assert.Equal(h.MeanPaceSecondsPerKm, h.RecentEasyPaceSecondsPerKm);
        Assert.NotNull(h.RecentEasyPaceSecondsPerKm);
    }

    [Fact]
    public void Recent_easy_pace_is_null_without_any_pace()
    {
        Assert.Null(TrainingHistory.From([], Today).RecentEasyPaceSecondsPerKm);
    }

    [Fact]
    public void Recent_easy_pace_with_an_even_count_is_the_mean_of_the_middle_two()
    {
        var facts = new List<RunFact> { Easy("2026-10-20", 1, seconds: 2_800), Easy("2026-10-27", 2, seconds: 2_880), Easy("2026-11-03", 3, seconds: 3_040), Easy("2026-11-05", 4, seconds: 3_200) };

        Assert.Equal(370, TrainingHistory.From(facts, Today).RecentEasyPaceSecondsPerKm); // 350, 360, 380, 400 -> (360 + 380) / 2 = 370
    }
}
