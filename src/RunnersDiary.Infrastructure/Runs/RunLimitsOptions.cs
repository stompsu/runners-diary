using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Infrastructure.Runs;

/// <summary>Per-account run caps (<c>RunLimits</c> section). Exceeding one is answered with 409 <c>run-limit</c>.</summary>
public sealed class RunLimitsOptions
{
    public const string SectionName = "RunLimits";

    public int GuestMaxRuns { get; set; } = RunLimitDefaults.GuestMaxRuns;

    public int UserMaxRuns { get; set; } = RunLimitDefaults.UserMaxRuns;

    public int MaxRunsPerDate { get; set; } = RunLimitDefaults.MaxRunsPerDate;
}
