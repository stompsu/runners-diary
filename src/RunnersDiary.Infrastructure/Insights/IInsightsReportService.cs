using RunnersDiary.Domain.Insights;

namespace RunnersDiary.Infrastructure.Insights;

/// <summary>
/// Computes the caller's <see cref="InsightsReport"/> on demand (ADR 0025). Everything is read through the owner-filtered
/// context, so a report can only ever describe the current caller's data; the M6 agent tools use this same service in-process.
/// </summary>
public interface IInsightsReportService
{
    /// <param name="firstDay">First day of the period (local date).</param>
    /// <param name="lastDay">Last day of the period (inclusive); the caller validates the span (<see cref="InsightsPeriod.MaxDays"/>).</param>
    /// <param name="compare">Which comparison window to total as well.</param>
    Task<InsightsReport> GetReportAsync(DateOnly firstDay, DateOnly lastDay, ComparisonKind compare, CancellationToken cancellationToken);
}
