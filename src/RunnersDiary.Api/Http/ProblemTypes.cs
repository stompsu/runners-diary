namespace RunnersDiary.Api.Http;

/// <summary>Stable problem <c>type</c> URNs (api-contract.md). Statuses without an entry use the default RFC 9110 type.</summary>
public static class ProblemTypes
{
    public const string ConcurrencyConflict = "urn:runnersdiary:problem:concurrency-conflict";
    public const string RateLimited = "urn:runnersdiary:problem:rate-limited";
    public const string RunLimit = "urn:runnersdiary:problem:run-limit";
    public const string GuestCapacity = "urn:runnersdiary:problem:guest-capacity";
    public const string InvalidGpx = "urn:runnersdiary:problem:invalid-gpx";
    public const string TrackExists = "urn:runnersdiary:problem:track-exists";
    public const string TrackLimit = "urn:runnersdiary:problem:track-limit";
    public const string ProcessingBusy = "urn:runnersdiary:problem:processing-busy";
    public const string FeatureUnavailable = "urn:runnersdiary:problem:feature-unavailable";
    public const string AiQuota = "urn:runnersdiary:problem:ai-quota";
    public const string AiBusy = "urn:runnersdiary:problem:ai-busy";
    public const string SummaryInProgress = "urn:runnersdiary:problem:summary-in-progress";
    public const string SummaryFailed = "urn:runnersdiary:problem:summary-failed";
    public const string StorageUnavailable = "urn:runnersdiary:problem:storage-unavailable";
}
