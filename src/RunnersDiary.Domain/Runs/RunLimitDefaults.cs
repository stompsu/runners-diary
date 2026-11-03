namespace RunnersDiary.Domain.Runs;

/// <summary>Default per-account run caps (abuse and cost bound). The API makes them configurable; the demo dataset must fit the guest caps.</summary>
public static class RunLimitDefaults
{
    public const int GuestMaxRuns = 500;

    public const int UserMaxRuns = 20_000;

    public const int MaxRunsPerDate = 20;
}
