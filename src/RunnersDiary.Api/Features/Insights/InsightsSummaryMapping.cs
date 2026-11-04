using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Api.Features.Insights;

internal static class InsightsSummaryMapping
{
    public static InsightsSummaryResponse ToResponse(SummaryView view) => new()
    {
        Period = new SummaryPeriodDto { From = view.Key.From, To = view.Key.To, Compare = CompareName(view.Key.Compare), Lang = view.Key.Language },
        State = view.State switch
        {
            SummaryState.None => SummaryStateDto.None,
            SummaryState.Fresh => SummaryStateDto.Fresh,
            _ => SummaryStateDto.Stale,
        },
        Summary = view.Summary is { } summary ? ToDto(summary) : null,
        Generation = new SummaryGenerationDto
        {
            Availability = view.Generation.Availability switch
            {
                GenerationAvailability.Available => GenerationAvailabilityDto.Available,
                GenerationAvailability.NotConfigured => GenerationAvailabilityDto.NotConfigured,
                GenerationAvailability.QuotaExhausted => GenerationAvailabilityDto.QuotaExhausted,
                _ => GenerationAvailabilityDto.NotNeeded,
            },
            Used = view.Generation.Used,
            Limit = view.Generation.Limit,
            ResetsAt = view.Generation.ResetsAt,
            Scope = view.Generation.Scope switch
            {
                QuotaWindow.Rolling24h => QuotaScopeDto.Rolling24h,
                QuotaWindow.Account => QuotaScopeDto.Account,
                _ => null,
            },
        },
    };

    public static string CompareName(ComparisonKind kind) => kind switch
    {
        ComparisonKind.None => GetInsightsReport.CompareNone,
        ComparisonKind.PreviousYear => GetInsightsReport.ComparePreviousYear,
        _ => GetInsightsReport.ComparePrevious,
    };

    private static InsightsSummaryDto ToDto(SummaryResult summary)
    {
        var content = summary.Content;
        return new InsightsSummaryDto
        {
            Source = summary.Source switch
            {
                SummarySource.Model => SummarySourceDto.Model,
                SummarySource.Demo => SummarySourceDto.Demo,
                _ => SummarySourceDto.Template,
            },
            CreatedAt = summary.CreatedAt,
            MetricsVersion = summary.MetricsVersion,
            FactSheetVersion = summary.FactSheetVersion,
            PromptVersion = summary.PromptVersion,
            Model = summary.Model,
            Headline = ToDto(content.Headline),
            WentWell = [.. content.WentWell.Select(ToDto)],
            WatchOuts = [.. content.WatchOuts.Select(ToDto)],
            Suggestion = content.Suggestion is { } suggestion ? ToDto(suggestion) : null,
            DataNotes = [.. content.DataNotes.Select(ToDto)],
            Values = content.Values.ToDictionary(
                v => v.Key,
                v => new SummaryValueDto { Number = v.Value.Number, Text = v.Value.Text, Unit = Enum.Parse<SummaryUnitDto>(v.Value.Unit.ToString()) },
                StringComparer.Ordinal),
        };
    }

    private static SummaryTextDto ToDto(SummaryText text) => new() { Text = text.Text, Keys = text.Keys };
}
