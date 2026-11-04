using RunnersDiary.Api.Features.Calendar;
using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Insights;

namespace RunnersDiary.Api.Features.Insights;

internal static class InsightsMapping
{
    public static InsightsReportResponse ToResponse(InsightsReport r) => new()
    {
        MetricsVersion = r.MetricsVersion,
        Today = r.Today,
        Period = new InsightsPeriodDto
        {
            From = r.Period.From,
            To = r.Period.To,
            AsOf = r.Period.AsOf,
            IsPartial = r.Period.IsPartial,
            Days = r.Period.Days,
            ElapsedDays = r.Period.ElapsedDays,
        },
        Thresholds = new InsightsThresholdsDto
        {
            AcwrLow = r.Thresholds.AcwrLow,
            AcwrHigh = r.Thresholds.AcwrHigh,
            MonotonyHigh = r.Thresholds.MonotonyHigh,
            AcwrMinHistoryDays = r.Thresholds.AcwrMinHistoryDays,
        },
        Totals = ToDto(r.Totals),
        Planned = new PlannedSummaryDto
        {
            RunCount = r.Planned.RunCount,
            DistanceMeters = r.Planned.DistanceMeters,
            DurationSeconds = r.Planned.DurationSeconds,
            UpcomingCount = r.Planned.UpcomingCount,
            MissedCount = r.Planned.MissedCount,
        },
        Comparison = r.Comparison is { } c
            ? new InsightsComparisonDto { Kind = ToDto(c.Kind), From = c.From, To = c.To, Totals = ToDto(c.Totals), Deltas = ToDto(c.Deltas) }
            : null,
        Load = new InsightsLoadDto
        {
            HistoryStart = r.Load.HistoryStart,
            AtAsOf = ToDto(r.Load.AtAsOf),
            Peak = r.Load.Peak is { } peak ? ToDto(peak) : null,
            BandDays = new BandDaysDto
            {
                Low = r.Load.BandDays.Low,
                Optimal = r.Load.BandDays.Optimal,
                High = r.Load.BandDays.High,
                Unavailable = r.Load.BandDays.Unavailable,
            },
            Daily = [.. r.Load.Daily.Select(d => new DailyLoadDto
            {
                Date = d.Date,
                Load = d.Load,
                Acute = d.Acute,
                Chronic = d.Chronic,
                Acwr = d.Acwr,
                Band = d.Band is { } band ? ToDto(band) : null,
            })],
        },
        Weeks = [.. r.Weeks.Select(w => new InsightsWeekDto
        {
            IsoYear = w.IsoYear,
            IsoWeek = w.IsoWeek,
            Start = w.Start,
            End = w.End,
            Completed = ToDto(w.Completed),
            Planned = ToDto(w.Planned),
            Load = w.Load,
            Monotony = w.Monotony,
            Strain = w.Strain,
            AcwrAtEnd = w.AcwrAtEnd,
            Complete = w.Complete,
        })],
        Months = [.. r.Months.Select(m => new InsightsMonthDto
        {
            Year = m.Year,
            Month = m.Month,
            Start = m.Start,
            End = m.End,
            Completed = ToDto(m.Completed),
            Planned = ToDto(m.Planned),
            Load = m.Load,
        })],
        TypeDistribution = [.. r.TypeDistribution.Select(t => new TypeShareDto
        {
            RunTypeId = t.RunTypeId,
            Code = t.Code,
            Category = t.Category,
            RunCount = t.RunCount,
            DistanceMeters = t.DistanceMeters,
            DurationSeconds = t.DurationSeconds,
            Load = t.Load,
        })],
        PaceTrend = new PaceTrendDto
        {
            RunTypeCodes = r.PaceTrend.RunTypeCodes,
            SlopeSecondsPerKmPerWeek = r.PaceTrend.SlopeSecondsPerKmPerWeek,
            ExcludedRunCount = r.PaceTrend.ExcludedRunCount,
            Weeks = [.. r.PaceTrend.Weeks.Select(w => new PaceWeekDto
            {
                Start = w.Start,
                RunCount = w.RunCount,
                DistanceMeters = w.DistanceMeters,
                DurationSeconds = w.DurationSeconds,
                PaceSecondsPerKm = w.PaceSecondsPerKm,
                AvgHeartRate = w.AvgHeartRate,
            })],
        },
        Races = [.. r.Races.Select(x => new RaceResultDto
        {
            RunId = x.RunId,
            Date = x.Date,
            Status = x.Status,
            RunTypeCode = x.RunTypeCode,
            Name = x.Name,
            DistanceCategory = x.DistanceCategory,
            OfficialDistanceMeters = x.OfficialDistanceMeters,
            DistanceMeters = x.DistanceMeters,
            ResultTimeSeconds = x.ResultTimeSeconds,
            TargetTimeSeconds = x.TargetTimeSeconds,
            PaceSecondsPerKm = x.PaceSecondsPerKm,
            OverallPlace = x.OverallPlace,
            CategoryPlace = x.CategoryPlace,
            CategoryName = x.CategoryName,
            RatingStars = x.RatingStars,
            IsPersonalBest = x.IsPersonalBest,
        })],
        DataQuality = new DataQualityDto
        {
            CompletedSessions = r.DataQuality.CompletedSessions,
            SessionsWithEstimatedRpe = r.DataQuality.SessionsWithEstimatedRpe,
            EstimatedLoadShare = r.DataQuality.EstimatedLoadShare,
            SessionsWithoutDuration = r.DataQuality.SessionsWithoutDuration,
            RunningSessionsWithoutDistance = r.DataQuality.RunningSessionsWithoutDistance,
        },
    };

    private static PeriodTotalsDto ToDto(PeriodTotals t) => new()
    {
        RunCount = t.RunCount,
        DistanceMeters = t.DistanceMeters,
        DurationSeconds = t.DurationSeconds,
        ElevationGainMeters = t.ElevationGainMeters,
        AveragePaceSecondsPerKm = t.AveragePaceSecondsPerKm,
        LongestRunMeters = t.LongestRunMeters,
        Load = t.Load,
        LoadSessionCount = t.LoadSessionCount,
        ActiveDays = t.ActiveDays,
        RestDays = t.RestDays,
    };

    private static TotalsDto ToDto(Totals t) => new()
    {
        RunCount = t.RunCount,
        DistanceMeters = t.DistanceMeters,
        DurationSeconds = t.DurationSeconds,
        ElevationGainMeters = t.ElevationGainMeters,
    };

    private static DeltasDto ToDto(Deltas d) => new()
    {
        RunCount = ToDto(d.RunCount),
        DistanceMeters = ToDto(d.DistanceMeters),
        DurationSeconds = ToDto(d.DurationSeconds),
        ElevationGainMeters = ToDto(d.ElevationGainMeters),
        Load = ToDto(d.Load),
        ActiveDays = ToDto(d.ActiveDays),
    };

    private static DeltaDto ToDto(Delta d) => new() { Absolute = d.Absolute, Percent = d.Percent };

    private static AcwrPointDto ToDto(AcwrPoint p) => new()
    {
        Date = p.Date,
        Value = p.Value,
        Band = p.Band is { } band ? ToDto(band) : null,
        Acute = p.Acute,
        Chronic = p.Chronic,
        UnavailableReason = p.UnavailableReason switch
        {
            AcwrUnavailableReason.InsufficientHistory => AcwrUnavailableReasonDto.InsufficientHistory,
            AcwrUnavailableReason.NoChronicLoad => AcwrUnavailableReasonDto.NoChronicLoad,
            _ => null,
        },
    };

    private static BandDto ToDto(AcwrBand band) => band switch
    {
        AcwrBand.Low => BandDto.Low,
        AcwrBand.Optimal => BandDto.Optimal,
        _ => BandDto.High,
    };

    private static ComparisonKindDto ToDto(ComparisonKind kind) =>
        kind == ComparisonKind.PreviousYear ? ComparisonKindDto.PreviousYear : ComparisonKindDto.Previous;
}
