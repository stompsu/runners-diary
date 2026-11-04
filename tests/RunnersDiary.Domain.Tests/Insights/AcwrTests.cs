using RunnersDiary.Domain.Insights;
using static RunnersDiary.Domain.Tests.Insights.InsightsTestData;

namespace RunnersDiary.Domain.Tests.Insights;

public sealed class AcwrTests
{
    private static readonly DateOnly Start = D("2026-03-02");

    private static DailyLoadSeries Series(IEnumerable<RunFact> facts, DateOnly end) =>
        new(facts, Start.AddDays(-34), end, Start);

    [Fact]
    public void Constant_load_gives_a_ratio_of_one()
    {
        var series = Series(Daily(Start, 40, 50), Start.AddDays(39));

        var point = series.At(Start.AddDays(39));

        Assert.Equal(1.00, point.Value);
        Assert.Equal(AcwrBand.Optimal, point.Band);
        Assert.Equal(350.0, point.Acute);
        Assert.Equal(350.0, point.Chronic);
    }

    [Fact]
    public void A_spike_is_uncoupled_28_days_at_40_then_7_days_at_60_is_1_50_high()
    {
        var facts = Daily(Start, 28, 40).Concat(Daily(Start.AddDays(28), 7, 60));
        var series = Series(facts, Start.AddDays(34));

        var point = series.At(Start.AddDays(34));

        Assert.Equal(420.0, point.Acute);
        Assert.Equal(280.0, point.Chronic);
        Assert.Equal(1.50, point.Value);
        Assert.Equal(AcwrBand.High, point.Band);
        Assert.NotEqual(1.33, point.Value); // the coupled formula would give 420 / ((840 + 420) / 4)
    }

    [Fact]
    public void Day_34_of_history_is_not_enough_and_day_35_is()
    {
        var series = Series(Daily(Start, 40, 50), Start.AddDays(39));

        var tooEarly = series.At(Start.AddDays(33));
        var enough = series.At(Start.AddDays(34));

        Assert.Equal(AcwrUnavailableReason.InsufficientHistory, tooEarly.UnavailableReason);
        Assert.Null(tooEarly.Value);
        Assert.Null(tooEarly.Band);
        Assert.Null(tooEarly.Chronic);
        Assert.Null(enough.UnavailableReason);
        Assert.Equal(1.00, enough.Value);
    }

    [Fact]
    public void Unknown_history_start_means_insufficient_history_never_rest()
    {
        var series = new DailyLoadSeries(Daily(Start, 40, 50), Start.AddDays(-34), Start.AddDays(39), historyStart: null);

        Assert.Equal(AcwrUnavailableReason.InsufficientHistory, series.At(Start.AddDays(39)).UnavailableReason);
    }

    [Fact]
    public void A_four_week_break_leaves_no_chronic_load()
    {
        // One training week, then nothing: from day 41 the chronic window (days d-34..d-7) is entirely after it.
        var series = Series(Daily(Start, 7, 50), Start.AddDays(41));

        var point = series.At(Start.AddDays(41));

        Assert.Equal(AcwrUnavailableReason.NoChronicLoad, point.UnavailableReason);
        Assert.Null(point.Value);
        Assert.Null(point.Band);
        Assert.Equal(0.0, point.Chronic);
        Assert.Equal(0.0, point.Acute);
    }

    [Theory]
    [InlineData(7949, 40000, 0.79, AcwrBand.Low)]
    [InlineData(790, 4000, 0.79, AcwrBand.Low)]
    [InlineData(795, 4000, 0.80, AcwrBand.Optimal)] // 0.795 rounds away from zero to 0.80
    [InlineData(1300, 4000, 1.30, AcwrBand.Optimal)]
    [InlineData(1305, 4000, 1.31, AcwrBand.High)] // exactly 1.305 on integer sums
    [InlineData(1000, 4000, 1.00, AcwrBand.Optimal)]
    public void Bands_come_from_the_rounded_ratio(long acuteSum, long chronicSum, double expected, AcwrBand band)
    {
        var ratio = Acwr.Ratio(acuteSum, chronicSum);

        Assert.Equal((decimal)expected, ratio);
        Assert.Equal(band, Acwr.BandOf(ratio!.Value));
    }

    [Fact]
    public void No_chronic_sum_gives_no_ratio() => Assert.Null(Acwr.Ratio(100, 0));

    [Fact]
    public void Peak_ties_resolve_to_the_earliest_date_and_band_days_cover_every_elapsed_day()
    {
        // Constant 50 AU for 80 days: every ACWR in the last 14 days is 1.00.
        var facts = Daily(Start, 80, 50).ToList();
        var to = Start.AddDays(79);
        var from = to.AddDays(-13);

        var report = Build(from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), "2030-01-01", facts);

        Assert.Equal(from, report.Load.Peak!.Date);
        Assert.Equal(1.00, report.Load.Peak.Value);
        Assert.Equal(new BandDays(0, 14, 0, 0), report.Load.BandDays);
        Assert.Equal(report.Period.ElapsedDays, report.Load.BandDays.Low + report.Load.BandDays.Optimal + report.Load.BandDays.High + report.Load.BandDays.Unavailable);
        Assert.Equal(Start, report.Load.HistoryStart);
        Assert.Equal(1.00, report.Load.AtAsOf.Value);
    }

    [Fact]
    public void Early_diary_days_count_as_unavailable_in_band_days()
    {
        var facts = Daily(Start, 20, 50).ToList();

        var report = Build("2026-03-02", "2026-03-21", "2026-03-21", facts);

        Assert.Equal(new BandDays(0, 0, 0, 20), report.Load.BandDays);
        Assert.Null(report.Load.Peak);
        Assert.Equal(AcwrUnavailableReason.InsufficientHistory, report.Load.AtAsOf.UnavailableReason);
    }
}
