namespace RunnersDiary.Api.Http;

/// <summary>Stable problem <c>type</c> URNs (api-contract.md). Statuses without an entry use the default RFC 9110 type.</summary>
public static class ProblemTypes
{
    public const string ConcurrencyConflict = "urn:runnersdiary:problem:concurrency-conflict";
    public const string RateLimited = "urn:runnersdiary:problem:rate-limited";
    public const string RunLimit = "urn:runnersdiary:problem:run-limit";
    public const string GuestCapacity = "urn:runnersdiary:problem:guest-capacity";
}
