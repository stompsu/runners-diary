using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Infrastructure.RaceSources;
using RunnersDiary.Infrastructure.RaceSources.RunningLife;

namespace RunnersDiary.Api.Tests.RaceSources;

/// <summary>A scripted HTTP server: routes by absolute URL (without the query for robots), records every request with the fake clock's reading.</summary>
internal sealed class FakeRaceHandler(FakeTimeProvider clock) : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    /// <summary>Answers a request; the default is 404.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(new RecordedRequest(
            request.Method, request.RequestUri!, clock.GetUtcNow(),
            request.Headers.UserAgent.ToString(), request.Headers.AcceptEncoding.ToString(),
            request.Headers.TryGetValues("If-None-Match", out var inm) ? inm.Single() : null,
            request.Headers.TryGetValues("If-Modified-Since", out var ims) ? ims.Single() : null));
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return Respond(request);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Url, DateTimeOffset At, string UserAgent, string AcceptEncoding, string? IfNoneMatch, string? IfModifiedSince);

internal static class RaceSourceTestSupport
{
    private static readonly string Fixtures = FindUp("tests", "fixtures", "races");

    public const string Host = "running.life";

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(Fixtures, name));

    public static RaceImportOptions Options(Action<RaceImportOptions>? change = null)
    {
        var options = new RaceImportOptions { Enabled = true, Sources = { RunningLife = { Enabled = true } } };
        change?.Invoke(options);
        return options;
    }

    public static HttpResponseMessage Html(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    public static PoliteHttpClient Polite(HttpMessageHandler handler, FakeTimeProvider clock, RaceImportOptions? options = null) =>
        new(new HttpClient(handler), Host, "running-life", options ?? Options(), clock, NullLogger.Instance);

    public static RunningLifeSource Source(HttpMessageHandler handler, FakeTimeProvider clock, RaceImportOptions? options = null) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options()), clock, NullLogger<RunningLifeSource>.Instance);

    /// <summary>Runs <paramref name="work"/> while moving the fake clock forward, so throttling delays and timeouts elapse without real waiting.</summary>
    public static async Task<T> WithClockAsync<T>(FakeTimeProvider clock, Func<Task<T>> work)
    {
        var task = work();
        while (!task.IsCompleted)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        return await task;
    }

    public static async Task WithClockAsync(FakeTimeProvider clock, Func<Task> work) =>
        await WithClockAsync(clock, async () =>
        {
            await work();
            return 0;
        });

    /// <summary>A listing page with the given events (path, name, date) and pagination links up to <paramref name="lastPage"/>.</summary>
    public static string ListingHtml(int lastPage, params (string Path, string Name, string Date)[] events)
    {
        const string Place = "\"location\":{\"@type\":\"Place\",\"name\":\"Gliwice\",\"address\":{\"@type\":\"PostalAddress\",\"addressLocality\":\"Gliwice\","
            + "\"addressRegion\":\"Silesian Voivodeship\",\"addressCountry\":\"Polska\"},\"geo\":{\"@type\":\"GeoCoordinates\",\"latitude\":50.2945,\"longitude\":18.6714}}";
        var items = string.Join(",", events.Select((e, i) =>
            "{\"@type\":\"ListItem\",\"position\":" + (i + 1) + ",\"item\":{\"@type\":\"SportsEvent\",\"name\":\"" + e.Name + "\",\"description\":\"invented\","
            + "\"url\":\"https://running.life/pl/wydarzenie/" + e.Path + "\",\"startDate\":\"" + e.Date + "\"," + Place + "}}"));
        var links = string.Join(string.Empty, Enumerable.Range(1, lastPage).Select(p => "<a href=\"https://running.life/kalendarz-biegow/polska?page=" + p + "\">" + p + "</a>"));
        return "<html><head><script type=\"application/ld+json\">{\"@type\":\"ItemList\",\"itemListElement\":[" + items + "]}</script></head><body>" + links + "</body></html>";
    }

    public static string DetailHtml(string chips = "10 km", string website = "https://club.example/x", string? registration = "https://zapisy.example/x") =>
        $$"""
        <html><body><h1>x</h1>
        <a href="{{website}}" data-out="website">Strona</a>
        {{(registration is null ? string.Empty : $"<a href=\"{registration}\" data-out=\"registration\">Rejestracja</a>")}}
        <div>{{string.Join(string.Empty, chips.Split('|').Select(c => $"<span class=\"rounded-full\">{c}</span>"))}}</div>
        </body></html>
        """;

    public static string RequestPath(HttpRequestMessage request) => request.RequestUri!.AbsolutePath;

    private static string FindUp(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine([dir.FullName, .. parts]);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(string.Join('/', parts));
    }
}
