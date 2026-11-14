using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace RunnersDiary.Infrastructure.RaceSources;

/// <summary>How a fetch ended when it did not stop the source.</summary>
public enum PoliteOutcome
{
    /// <summary>2xx with a body within the cap.</summary>
    Ok = 0,

    /// <summary>304 for a conditional request: the stored facts stay.</summary>
    NotModified = 1,

    /// <summary>A 4xx other than 403/429 (a missing page); the caller skips it.</summary>
    ClientError = 2,

    /// <summary>A 5xx, a timeout or a network error (counted: three in a row stop the source).</summary>
    ServerError = 3,

    /// <summary>The body exceeded the size cap and was dropped.</summary>
    TooLarge = 4,

    /// <summary>The URL or a redirect target left the allowed host or was not https, or redirects looped.</summary>
    Refused = 5,
}

/// <summary>Validators of a stored page, sent as <c>If-None-Match</c> / <c>If-Modified-Since</c>.</summary>
public sealed record ConditionalRequest(string? ETag, string? LastModified);

public sealed record PoliteResponse(PoliteOutcome Outcome, HttpStatusCode? Status, string? Body, string? ETag, string? LastModified)
{
    public bool IsOk => Outcome == PoliteOutcome.Ok;
}

/// <summary>Raised when a stop rule ends the source for this run (403/429, three consecutive failures, robots.txt). Never retried.</summary>
internal sealed class SourceStopException(RaceScanOutcome outcome, string reason) : Exception(reason)
{
    public RaceScanOutcome Outcome { get; } = outcome;
}

/// <summary>
/// The one way the importer talks to a source (ADR 0035 §4), unit-tested with a fake handler and a fake clock. One instance serves one scan of one host and is used by one caller at a time (the importer is sequential by design):
/// identifying User-Agent, <c>robots.txt</c> loaded first and honoured for every path, a gap of at least <c>MinInterval</c> between requests (one at a time),
/// GET and https only, a timeout, a decompressed body cap, redirects only to the same host, conditional requests, and the stop rules
/// (403/429 stop at once, three consecutive 5xx or timeouts stop, nothing is retried).
/// Logs paths only: no query strings and no page bodies.
/// </summary>
internal sealed partial class PoliteHttpClient
{
    private const int MaxRedirects = 3;
    private const int MaxConsecutiveFailures = 3;

    private readonly HttpClient _http;
    private readonly string _host;
    private readonly string _sourceId;
    private readonly RaceImportOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;

    private DateTimeOffset? _lastRequest;
    private int _consecutiveFailures;
    private RobotsTxt _robots = RobotsTxt.AllowAll;

    public PoliteHttpClient(HttpClient http, string host, string sourceId, RaceImportOptions options, TimeProvider clock, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _host = host;
        _sourceId = sourceId;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Requests sent so far (robots.txt and redirect hops included).</summary>
    public int Requests { get; private set; }

    /// <summary>
    /// Fetches robots.txt (RFC 9309): 2xx is parsed, other 4xx allow everything, 5xx / timeout / oversize / unparsable abort the source,
    /// and 403/429 follow the stop rule. Call once, before the first <see cref="GetAsync"/>.
    /// </summary>
    public async Task LoadRobotsAsync(CancellationToken cancellationToken)
    {
        var response = await FetchAsync(new Uri($"https://{_host}/robots.txt"), conditional: null, checkRobots: false, cancellationToken);
        switch (response.Outcome)
        {
            case PoliteOutcome.Ok:
                if (!RobotsTxt.TryParse(response.Body ?? string.Empty, RaceImportOptions.ProductToken, out var parsed))
                {
                    LogRobotsUnparsable(_sourceId);
                    throw new SourceStopException(RaceScanOutcome.RobotsUnavailable, "robots.txt is not parsable");
                }

                _robots = parsed;
                foreach (var signal in parsed.ContentSignals)
                {
                    LogContentSignal(_sourceId, signal);
                }

                break;
            case PoliteOutcome.ClientError:
                _robots = RobotsTxt.AllowAll;
                LogRobotsMissing(_sourceId, (int?)response.Status ?? 0);
                break;
            default:
                LogRobotsUnavailable(_sourceId, response.Outcome);
                throw new SourceStopException(RaceScanOutcome.RobotsUnavailable, "robots.txt is unavailable");
        }
    }

    /// <summary>Whether robots.txt allows <paramref name="path"/> (path and query).</summary>
    public bool IsAllowed(string pathAndQuery) => _robots.IsAllowed(pathAndQuery);

    /// <summary>GET with the politeness rules. Throws <see cref="SourceStopException"/> when a stop rule applies.</summary>
    public Task<PoliteResponse> GetAsync(Uri url, ConditionalRequest? conditional, CancellationToken cancellationToken) =>
        FetchAsync(url, conditional, checkRobots: true, cancellationToken);

    private async Task<PoliteResponse> FetchAsync(Uri url, ConditionalRequest? conditional, bool checkRobots, CancellationToken cancellationToken)
    {
        var current = url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!IsAllowedTarget(current))
            {
                LogRefused(_sourceId, current.Host);
                return new PoliteResponse(PoliteOutcome.Refused, null, null, null, null);
            }

            if (checkRobots && !_robots.IsAllowed(current.PathAndQuery))
            {
                LogRobotsDisallowed(_sourceId, current.AbsolutePath);
                throw new SourceStopException(RaceScanOutcome.RobotsDisallowed, "A path is disallowed by robots.txt");
            }

            var (response, redirect) = await SendOnceAsync(current, conditional, cancellationToken);
            if (redirect is null)
            {
                return response;
            }

            current = redirect;
        }

        LogRefused(_sourceId, current.Host);
        return new PoliteResponse(PoliteOutcome.Refused, null, null, null, null);
    }

    private bool IsAllowedTarget(Uri url) =>
        url.IsAbsoluteUri
        && url.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(url.UserInfo)
        && url.IsDefaultPort
        && string.Equals(url.Host, _host, StringComparison.OrdinalIgnoreCase);

    private async Task<(PoliteResponse Response, Uri? Redirect)> SendOnceAsync(Uri url, ConditionalRequest? conditional, CancellationToken cancellationToken)
    {
        await ThrottleAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", RaceImportOptions.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,text/plain;q=0.9,*/*;q=0.5");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, br");
        if (conditional?.ETag is { Length: > 0 } etag)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        if (conditional?.LastModified is { Length: > 0 } lastModified)
        {
            request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
        }

        using var timeout = new CancellationTokenSource(_options.RequestTimeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var started = _clock.GetTimestamp();
        Requests++;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            return await ClassifyAsync(url, response, conditional is not null, started, linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(_sourceId, url.AbsolutePath);
            return (RecordFailure("timeout"), null);
        }
        catch (HttpRequestException ex)
        {
            LogNetworkError(_sourceId, url.AbsolutePath, ex.GetType().Name);
            return (RecordFailure("network"), null);
        }
    }

    private async Task<(PoliteResponse Response, Uri? Redirect)> ClassifyAsync(
        Uri url, HttpResponseMessage response, bool wasConditional, long started, CancellationToken cancellationToken)
    {
        var status = response.StatusCode;
        var code = (int)status;
        var elapsedMs = (long)_clock.GetElapsedTime(started).TotalMilliseconds;
        LogRequest(_sourceId, url.AbsolutePath, code, elapsedMs);
        RaceImportMetrics.RecordRequest(_sourceId, $"{code / 100}xx");
        if (response.Headers.TryGetValues("Content-Signal", out var signals))
        {
            var joined = string.Concat(string.Join(", ", signals).Where(c => !char.IsControl(c)));
            LogContentSignal(_sourceId, joined.Length > 200 ? joined[..200] : joined);
        }

        if (status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.ToString() ?? "none";
            LogStopped(_sourceId, code, retryAfter);
            throw new SourceStopException(RaceScanOutcome.StoppedByServer, "The source refused the request (403/429)");
        }

        if (code >= 500)
        {
            return (RecordFailure($"http {code}"), null);
        }

        _consecutiveFailures = 0;

        if (status == HttpStatusCode.NotModified)
        {
            return wasConditional
                ? (new PoliteResponse(PoliteOutcome.NotModified, status, null, null, null), null)
                : (new PoliteResponse(PoliteOutcome.ClientError, status, null, null, null), null);
        }

        if (code is 301 or 302 or 303 or 307 or 308)
        {
            var location = response.Headers.Location;
            if (location is null)
            {
                return (new PoliteResponse(PoliteOutcome.Refused, status, null, null, null), null);
            }

            return (new PoliteResponse(PoliteOutcome.Refused, status, null, null, null), location.IsAbsoluteUri ? location : new Uri(url, location));
        }

        if (code is < 200 or >= 300)
        {
            return (new PoliteResponse(PoliteOutcome.ClientError, status, null, null, null), null);
        }

        var body = await ReadBodyAsync(response, cancellationToken);
        if (body is null)
        {
            LogTooLarge(_sourceId, url.AbsolutePath, _options.MaxResponseBytes);
            return (new PoliteResponse(PoliteOutcome.TooLarge, status, null, null, null), null);
        }

        var etag = response.Headers.ETag?.ToString();
        var lastModified = response.Content.Headers.LastModified?.UtcDateTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return (new PoliteResponse(PoliteOutcome.Ok, status, body, etag, lastModified), null);
    }

    /// <summary>The decoded body, or null when it is larger than the cap (checked on the decompressed bytes).</summary>
    private async Task<string?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var cap = _options.MaxResponseBytes;
        if (response.Content.Headers.ContentLength is { } declared && declared > cap)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > cap)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return ResponseEncoding(response).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>The response's declared charset when .NET knows it, else UTF-8.</summary>
    private static System.Text.Encoding ResponseEncoding(HttpResponseMessage response)
    {
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"', ' ');
        if (!string.IsNullOrEmpty(charset))
        {
            try
            {
                return System.Text.Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                // Unknown charset name: fall through to UTF-8.
            }
        }

        return System.Text.Encoding.UTF8;
    }

    private PoliteResponse RecordFailure(string reason)
    {
        _consecutiveFailures++;
        RaceImportMetrics.RecordRequest(_sourceId, "error");
        if (_consecutiveFailures >= MaxConsecutiveFailures)
        {
            LogTooManyFailures(_sourceId, _consecutiveFailures, reason);
            throw new SourceStopException(RaceScanOutcome.StoppedByFailures, "Too many consecutive failures");
        }

        return new PoliteResponse(PoliteOutcome.ServerError, null, null, null, null);
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        if (_lastRequest is { } last)
        {
            var wait = last + _options.MinInterval - _clock.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _clock, cancellationToken);
            }
        }

        _lastRequest = _clock.GetUtcNow();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Race source {Source}: GET {Path} -> {Status} in {ElapsedMs} ms.")]
    private partial void LogRequest(string source, string path, int status, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Race source {Source}: stopped by HTTP {Status} (Retry-After: {RetryAfter}); no retry.")]
    private partial void LogStopped(string source, int status, string retryAfter);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Race source {Source}: request to {Path} timed out.")]
    private partial void LogTimeout(string source, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Race source {Source}: request to {Path} failed ({ErrorType}).")]
    private partial void LogNetworkError(string source, string path, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Race source {Source}: stopped after {Failures} consecutive failures (last: {Reason}).")]
    private partial void LogTooManyFailures(string source, int failures, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Race source {Source}: response for {Path} exceeds {Cap} bytes and was dropped.")]
    private partial void LogTooLarge(string source, string path, int cap);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Race source {Source}: refused a request or redirect to host {Host} (only https on the source's own host is allowed).")]
    private partial void LogRefused(string source, string host);

    [LoggerMessage(Level = LogLevel.Error, Message = "Race source {Source}: robots.txt disallows {Path}; aborting the source.")]
    private partial void LogRobotsDisallowed(string source, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Race source {Source}: robots.txt could not be parsed; aborting the source.")]
    private partial void LogRobotsUnparsable(string source);

    [LoggerMessage(Level = LogLevel.Error, Message = "Race source {Source}: robots.txt unavailable ({Outcome}); aborting the source.")]
    private partial void LogRobotsUnavailable(string source, PoliteOutcome outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Race source {Source}: robots.txt answered {Status}; everything is allowed.")]
    private partial void LogRobotsMissing(string source, int status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Race source {Source}: robots.txt Content-Signal: {Signal}.")]
    private partial void LogContentSignal(string source, string signal);
}
