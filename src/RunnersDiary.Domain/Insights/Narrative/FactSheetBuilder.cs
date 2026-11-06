using System.Globalization;
using System.Text;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Insights.Narrative;

/// <summary>
/// Projects an <see cref="InsightsReport"/> into the model-facing <see cref="FactSheet"/> (ADR 0026 §1). Values are copied verbatim
/// with the report's rounding; the only additions are annotations: list lengths, bucket indexes, race-week joins, the band
/// of a bucket's <c>acwrAtEnd</c> (the Domain band rule) and, since <c>factSheetVersion</c> 2, two week flags derived from that band
/// and the race list: <c>week.N.taper</c> (band low, a completed or planned race in the following ISO week) and
/// <c>week.N.loadSpike</c> (band high, no completed race in the week). It never computes a new number from metric values, and it
/// never copies free text (race names, places, category names), heart rate or ids.
/// </summary>
public static class FactSheetBuilder
{
    /// <summary>Weekly buckets up to this many weeks, monthly buckets above it.</summary>
    public const int MaxWeeklyBuckets = 16;

    /// <summary>At most this many races are itemised as <c>race.N.*</c> (the most recent ones); the counts cover all of them.</summary>
    public const int MaxListedRaces = 6;

    /// <summary>At most this many run types are itemised as <c>type.N.*</c> (they are ordered by duration, so these are the main ones).</summary>
    public const int MaxListedTypes = 8;

    public static FactSheet Build(InsightsReport report, string language)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var facts = new Writer();
        var period = report.Period;
        var future = period.AsOf < period.From;

        AddPeriod(facts, report, future);
        AddTotals(facts, report.Totals);
        AddComparison(facts, report.Comparison);
        AddPlanned(facts, report.Planned);
        AddLoad(facts, report, future);
        if (report.Weeks.Count <= MaxWeeklyBuckets)
        {
            AddWeeks(facts, report);
        }
        else
        {
            AddMonths(facts, report);
        }

        AddTypes(facts, report.TypeDistribution);
        AddPace(facts, report.PaceTrend);
        AddRaces(facts, report.Races);
        AddDataQuality(facts, report.DataQuality);
        AddThresholds(facts, report.Thresholds);

        return new FactSheet(FactSheet.CurrentVersion, report.MetricsVersion, language, facts.Facts);
    }

    private static void AddPeriod(Writer f, InsightsReport report, bool future)
    {
        var p = report.Period;
        f.Date("period.from", p.From);
        f.Date("period.to", p.To);
        if (!future)
        {
            f.Date("period.asOf", p.AsOf);
        }

        f.Flag("period.isPartial", p.IsPartial);
        f.Days("period.days", p.Days);
        f.Days("period.elapsedDays", p.ElapsedDays);
    }

    private static void AddTotals(Writer f, PeriodTotals t)
    {
        f.Count("totals.runCount", t.RunCount);
        f.Distance("totals.distance", t.DistanceMeters);
        f.Duration("totals.duration", t.DurationSeconds);
        f.Elevation("totals.elevationGain", t.ElevationGainMeters);
        if (t.AveragePaceSecondsPerKm is { } pace)
        {
            f.Pace("totals.averagePace", pace);
        }

        if (t.LongestRunMeters is { } longest)
        {
            f.Distance("totals.longestRun", longest);
        }

        f.Load("totals.load", t.Load);
        f.Count("totals.loadSessionCount", t.LoadSessionCount);
        f.Days("totals.activeDays", t.ActiveDays);
        f.Days("totals.restDays", t.RestDays);
    }

    private static void AddComparison(Writer f, ComparisonResult? comparison)
    {
        if (comparison is null)
        {
            return;
        }

        f.Date("comparison.from", comparison.From);
        f.Date("comparison.to", comparison.To);

        var d = comparison.Deltas;
        AddDelta(f, "runCount", d.RunCount, FactUnit.Count);
        AddDelta(f, "distance", d.DistanceMeters, FactUnit.Meters);
        AddDelta(f, "duration", d.DurationSeconds, FactUnit.Seconds);
        AddDelta(f, "elevationGain", d.ElevationGainMeters, FactUnit.Meters, elevation: true);
        AddDelta(f, "load", d.Load, FactUnit.Load);
        AddDelta(f, "activeDays", d.ActiveDays, FactUnit.Days);
    }

    private static void AddDelta(Writer f, string metric, Delta delta, FactUnit unit, bool elevation = false)
    {
        var key = $"comparison.delta.{metric}.absolute";
        var display = unit switch
        {
            FactUnit.Meters when elevation => Signed(delta.Absolute, Display.Elevation(Math.Abs(delta.Absolute))),
            FactUnit.Meters => Signed(delta.Absolute, Display.Distance(Math.Abs(delta.Absolute))),
            FactUnit.Seconds => Signed(delta.Absolute, Display.Duration(Math.Abs(delta.Absolute))),
            FactUnit.Load => Signed(delta.Absolute, Display.Load(Math.Abs(delta.Absolute))),
            FactUnit.Days => Signed(delta.Absolute, Display.DayCount(Math.Abs(delta.Absolute))),
            _ => Signed(delta.Absolute, Display.Number(Math.Abs(delta.Absolute))),
        };
        f.Add(key, FactValue.Of(delta.Absolute), unit, display);
        if (delta.Percent is { } percent)
        {
            f.Add($"comparison.delta.{metric}.percent", FactValue.Of(percent), FactUnit.Percent, Display.Percent(percent));
        }

        static string Signed(double value, string magnitude) => value > 0 ? "+" + magnitude : value < 0 ? "-" + magnitude : magnitude;
    }

    private static void AddPlanned(Writer f, PlannedSummary planned)
    {
        f.Count("planned.runCount", planned.RunCount);
        f.Distance("planned.distance", planned.DistanceMeters);
        f.Duration("planned.duration", planned.DurationSeconds);
        f.Count("planned.upcomingCount", planned.UpcomingCount);
        f.Count("planned.missedCount", planned.MissedCount);
    }

    private static void AddLoad(Writer f, InsightsReport report, bool future)
    {
        var load = report.Load;
        if (load.HistoryStart is { } historyStart)
        {
            f.Date("load.historyStart", historyStart);
        }

        // A wholly future period: ACWR "at asOf" is dated outside the period (ADR 0025 amendment), so it is omitted.
        if (!future)
        {
            var at = load.AtAsOf;
            f.Date("acwr.asOf.date", at.Date);
            f.Load("acwr.asOf.acute", at.Acute);
            if (at.Value is { } value)
            {
                f.Ratio("acwr.asOf.value", value);
                f.Band("acwr.asOf.band", at.Band!.Value);
                f.Load("acwr.asOf.chronic", at.Chronic!.Value);
            }
            else
            {
                f.Flag("acwr.asOf.unavailable", true);
                switch (at.UnavailableReason)
                {
                    case AcwrUnavailableReason.InsufficientHistory:
                        f.Flag("acwr.asOf.insufficientHistory", true);
                        break;
                    case AcwrUnavailableReason.NoChronicLoad:
                        f.Flag("acwr.asOf.noChronicLoad", true);
                        break;
                    default:
                        break;
                }
            }
        }

        if (load.Peak is { Value: { } peakValue } peak)
        {
            f.Date("acwr.peak.date", peak.Date);
            f.Ratio("acwr.peak.value", peakValue);
            f.Band("acwr.peak.band", peak.Band!.Value);
            f.Load("acwr.peak.acute", peak.Acute);
            if (peak.Chronic is { } chronic)
            {
                f.Load("acwr.peak.chronic", chronic);
            }

            if (report.Weeks.Count <= MaxWeeklyBuckets)
            {
                var index = IndexOf(report.Weeks, w => w.Start <= peak.Date && peak.Date <= w.End);
                if (index > 0)
                {
                    f.Count("acwr.peak.week", index);
                }
            }
            else
            {
                var index = IndexOf(report.Months, m => m.Start <= peak.Date && peak.Date <= m.End);
                if (index > 0)
                {
                    f.Count("acwr.peak.month", index);
                }
            }

            // A race inside the peak's acute window (the 7 days ending on the peak date) drives the ratio: it is a race week,
            // not a training spike (ADR 0024 note for M6).
            var race = report.Races
                .Where(r => r.Status == RunStatus.Completed && r.Date <= peak.Date && r.Date >= peak.Date.AddDays(-6))
                .OrderBy(r => r.Date)
                .LastOrDefault();
            f.Flag("acwr.peak.isRaceWeek", race is not null);
            if (race is not null)
            {
                f.RunType("acwr.peak.raceType", race.RunTypeCode);
            }
        }

        var bands = load.BandDays;
        f.Days("acwr.days.low", bands.Low);
        f.Days("acwr.days.optimal", bands.Optimal);
        f.Days("acwr.days.high", bands.High);
        f.Days("acwr.days.unavailable", bands.Unavailable);
    }

    private static void AddWeeks(Writer f, InsightsReport report)
    {
        f.Count("weeks.count", report.Weeks.Count);
        for (var i = 0; i < report.Weeks.Count; i++)
        {
            var w = report.Weeks[i];
            var k = $"week.{i + 1}";
            f.Date($"{k}.start", w.Start);
            if (w.Start > report.Today)
            {
                // A wholly future week: no completed data exists yet, only the plan.
                f.Flag($"{k}.future", true);
            }
            else
            {
                f.Distance($"{k}.distance", w.Completed.DistanceMeters);
                f.Duration($"{k}.duration", w.Completed.DurationSeconds);
                f.Load($"{k}.load", w.Load);
                if (w.AcwrAtEnd is { } acwr)
                {
                    f.Ratio($"{k}.acwrAtEnd", acwr);
                    f.Band($"{k}.acwrBand", Acwr.BandOf((decimal)acwr));
                }

                if (w.Monotony is { } monotony)
                {
                    f.Ratio($"{k}.monotony", monotony);
                }

                if (!w.Complete)
                {
                    // Only the exception is stated: the current week is still running.
                    f.Flag($"{k}.complete", false);
                }
            }

            if (w.Planned.RunCount > 0)
            {
                f.Count($"{k}.planned.runCount", w.Planned.RunCount);
                f.Distance($"{k}.planned.distance", w.Planned.DistanceMeters);
            }

            var race = report.Races.Where(r => r.Status == RunStatus.Completed && r.Date >= w.Start && r.Date <= w.End).OrderBy(r => r.Date).FirstOrDefault();
            if (race is not null)
            {
                f.RunType($"{k}.raceType", race.RunTypeCode);
            }

            // Interpretation flags (factSheetVersion 2), stated only when true; categorical joins of the band and the race list.
            if (w.Start <= report.Today && w.AcwrAtEnd is { } ratio)
            {
                var band = Acwr.BandOf((decimal)ratio);
                if (band == AcwrBand.Low && report.Races.Any(r => r.Date >= w.Start.AddDays(7) && r.Date <= w.End.AddDays(7)))
                {
                    // A low week right before a week with a race (completed or planned): a deliberate taper, not a problem.
                    f.Flag($"{k}.taper", true);
                }
                else if (band == AcwrBand.High && race is null)
                {
                    // A high ratio that no race in the week explains: a training load spike.
                    f.Flag($"{k}.loadSpike", true);
                }
            }
        }
    }

    private static void AddMonths(Writer f, InsightsReport report)
    {
        f.Count("months.count", report.Months.Count);
        for (var i = 0; i < report.Months.Count; i++)
        {
            var m = report.Months[i];
            var k = $"month.{i + 1}";
            f.Date($"{k}.start", m.Start);
            if (m.Start > report.Today)
            {
                f.Flag($"{k}.future", true);
            }
            else
            {
                f.Count($"{k}.runCount", m.Completed.RunCount);
                f.Distance($"{k}.distance", m.Completed.DistanceMeters);
                f.Duration($"{k}.duration", m.Completed.DurationSeconds);
                f.Load($"{k}.load", m.Load);
            }

            if (m.Planned.RunCount > 0)
            {
                f.Count($"{k}.planned.runCount", m.Planned.RunCount);
                f.Distance($"{k}.planned.distance", m.Planned.DistanceMeters);
            }
        }
    }

    private static void AddTypes(Writer f, IReadOnlyList<TypeShare> types)
    {
        f.Count("types.count", types.Count);
        for (var i = 0; i < Math.Min(types.Count, MaxListedTypes); i++)
        {
            var t = types[i];
            var k = $"type.{i + 1}";
            f.RunType($"{k}.code", t.Code);
            f.Count($"{k}.runCount", t.RunCount);
            f.Duration($"{k}.duration", t.DurationSeconds);
            f.Load($"{k}.load", t.Load);
        }
    }

    private static void AddPace(Writer f, PaceTrend pace)
    {
        f.Count("pace.pointCount", pace.Weeks.Count);
        f.Count("pace.excludedRunCount", pace.ExcludedRunCount);
        if (pace.Weeks.Count == 0)
        {
            return;
        }

        var first = pace.Weeks[0];
        var last = pace.Weeks[^1];
        f.Date("pace.first.week", first.Start);
        f.Pace("pace.first.pace", first.PaceSecondsPerKm);
        f.Date("pace.last.week", last.Start);
        f.Pace("pace.last.pace", last.PaceSecondsPerKm);
        if (pace.SlopeSecondsPerKmPerWeek is { } slope)
        {
            f.Add("pace.slope", FactValue.Of(slope), FactUnit.SecondsPerKmPerWeek, Display.Slope(slope));
        }
    }

    private static void AddRaces(Writer f, IReadOnlyList<RaceResult> all)
    {
        f.Count("races.count", all.Count);
        f.Count("races.completedCount", all.Count(r => r.Status == RunStatus.Completed));
        f.Count("races.plannedCount", all.Count(r => r.Status == RunStatus.Planned));

        // Only the most recent races are itemised (size cap); they keep chronological order and race.1 is the oldest listed.
        var races = all.Skip(Math.Max(0, all.Count - MaxListedRaces)).ToList();
        for (var i = 0; i < races.Count; i++)
        {
            var r = races[i];
            var k = $"race.{i + 1}";
            f.Date($"{k}.date", r.Date);
            f.Add($"{k}.status", FactValue.Of(StatusCode(r.Status)), FactUnit.RaceStatus, StatusCode(r.Status));
            f.RunType($"{k}.type", r.RunTypeCode);
            f.Add($"{k}.category", FactValue.Of(CategoryCode(r.DistanceCategory)), FactUnit.RaceCategory, Display.Category(r.DistanceCategory));
            if (r.ResultTimeSeconds is { } result)
            {
                f.Duration($"{k}.resultTime", result);
            }

            if (r.TargetTimeSeconds is { } target)
            {
                f.Duration($"{k}.targetTime", target);
            }

            if (r.PaceSecondsPerKm is { } pace)
            {
                f.Pace($"{k}.pace", pace);
            }

            if (r.Status == RunStatus.Completed)
            {
                f.Flag($"{k}.isPersonalBest", r.IsPersonalBest);
            }

            if (r.RatingStars is { } stars)
            {
                f.Add($"{k}.stars", FactValue.Of(stars), FactUnit.Stars, string.Create(CultureInfo.InvariantCulture, $"{stars} of 5"));
            }
        }
    }

    private static void AddDataQuality(Writer f, DataQuality dq)
    {
        f.Count("dq.completedSessions", dq.CompletedSessions);
        f.Count("dq.sessionsWithEstimatedRpe", dq.SessionsWithEstimatedRpe);
        f.Add("dq.estimatedLoadShare", FactValue.Of(dq.EstimatedLoadShare), FactUnit.Share, Display.Share(dq.EstimatedLoadShare));
        f.Count("dq.sessionsWithoutDuration", dq.SessionsWithoutDuration);
        f.Count("dq.runningSessionsWithoutDistance", dq.RunningSessionsWithoutDistance);
    }

    private static void AddThresholds(Writer f, InsightsThresholds t)
    {
        f.Ratio("thresholds.acwrLow", t.AcwrLow);
        f.Ratio("thresholds.acwrHigh", t.AcwrHigh);
        f.Ratio("thresholds.monotonyHigh", t.MonotonyHigh);
        f.Days("thresholds.acwrMinHistoryDays", t.AcwrMinHistoryDays);
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, Func<T, bool> predicate)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (predicate(list[i]))
            {
                return i + 1;
            }
        }

        return 0;
    }

    internal static string StatusCode(RunStatus status) => status == RunStatus.Completed ? "completed" : "planned";

    internal static string CategoryCode(RaceDistanceCategory category) => category switch
    {
        RaceDistanceCategory.FiveK => "fiveK",
        RaceDistanceCategory.TenK => "tenK",
        RaceDistanceCategory.HalfMarathon => "halfMarathon",
        RaceDistanceCategory.Marathon => "marathon",
        _ => "other",
    };

    private static string BandCode(AcwrBand band) => band switch
    {
        AcwrBand.Low => "low",
        AcwrBand.Optimal => "optimal",
        _ => "high",
    };

    /// <summary>Collects facts in emission order (the canonical order).</summary>
    private sealed class Writer
    {
        public List<Fact> Facts { get; } = [];

        public void Add(string key, FactValue value, FactUnit unit, string display) => Facts.Add(new Fact(key, value, unit, display));

        public void Date(string key, DateOnly date) => Add(key, FactValue.Of(date), FactUnit.Date, Display.Date(date));

        public void Flag(string key, bool flag) => Add(key, FactValue.Of(flag), FactUnit.Flag, flag ? "yes" : "no");

        public void Count(string key, double count) => Add(key, FactValue.Of(count), FactUnit.Count, Display.Number(count));

        public void Days(string key, double days) => Add(key, FactValue.Of(days), FactUnit.Days, Display.DayCount(days));

        public void Distance(string key, double meters) => Add(key, FactValue.Of(meters), FactUnit.Meters, Display.Distance(meters));

        public void Elevation(string key, double meters) => Add(key, FactValue.Of(meters), FactUnit.Meters, Display.Elevation(meters));

        public void Duration(string key, double seconds) => Add(key, FactValue.Of(seconds), FactUnit.Seconds, Display.Duration(seconds));

        public void Load(string key, double load) => Add(key, FactValue.Of(load), FactUnit.Load, Display.Load(load));

        public void Ratio(string key, double ratio) => Add(key, FactValue.Of(ratio), FactUnit.Ratio, ratio.ToString("0.00", CultureInfo.InvariantCulture));

        public void Pace(string key, double secondsPerKm) => Add(key, FactValue.Of(secondsPerKm), FactUnit.SecondsPerKm, Display.Pace(secondsPerKm));

        public void Band(string key, AcwrBand band) => Add(key, FactValue.Of(BandCode(band)), FactUnit.Band, BandCode(band));

        public void RunType(string key, string code) => Add(key, FactValue.Of(code), FactUnit.RunType, Display.RunType(code));
    }

    /// <summary>English metric display strings: they only tell the model the magnitude; the SPA formats the values itself.</summary>
    internal static class Display
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Number(double value) => value.ToString("0.##", Inv);

        public static string DayCount(double days) => days == 1 ? "1 day" : Number(days) + " days";

        public static string Distance(double meters) => (meters / 1000).ToString("0.0", Inv) + " km";

        public static string Elevation(double meters) => meters.ToString("0.#", Inv) + " m";

        public static string Load(double load) => load.ToString("0.0", Inv) + " AU";

        public static string Percent(double percent) => (percent > 0 ? "+" : string.Empty) + percent.ToString("0.0", Inv) + " %";

        public static string Share(double share) => (share * 100).ToString("0", Inv) + " %";

        public static string Slope(double slope) => slope.ToString("+0.00;-0.00;0.00", Inv) + " s/km per week";

        public static string Date(DateOnly date) => date.ToString("d MMM yyyy", Inv);

        public static string Duration(double seconds)
        {
            var total = (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
            var h = total / 3600;
            var m = total % 3600 / 60;
            var s = total % 60;
            return string.Create(Inv, $"{h}:{m:00}:{s:00}");
        }

        public static string Pace(double secondsPerKm)
        {
            var total = (long)Math.Round(secondsPerKm, MidpointRounding.AwayFromZero);
            return string.Create(Inv, $"{total / 60}:{total % 60:00} /km");
        }

        public static string Category(RaceDistanceCategory category) => category switch
        {
            RaceDistanceCategory.FiveK => "5K",
            RaceDistanceCategory.TenK => "10K",
            RaceDistanceCategory.HalfMarathon => "half marathon",
            RaceDistanceCategory.Marathon => "marathon",
            _ => "other distance",
        };

        /// <summary>A run-type code in words: <c>trailRace</c> → "trail race".</summary>
        public static string RunType(string code)
        {
            var builder = new StringBuilder(code.Length + 4);
            foreach (var c in code)
            {
                if (char.IsAsciiLetterUpper(c))
                {
                    builder.Append(' ').Append(char.ToLowerInvariant(c));
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
