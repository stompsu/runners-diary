using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Infrastructure.RaceSources;
using static RunnersDiary.Api.Tests.RaceSources.RaceSourceTestSupport;

namespace RunnersDiary.Api.Tests.RaceSources;

/// <summary>ADR 0035 §4: identification, robots.txt, spacing, stop rules, redirects, size cap, conditional requests, host allow-list.</summary>
public sealed class PoliteHttpClientTests
{
    private static readonly Uri Listing = new("https://running.life/kalendarz-biegow/polska?page=1");
    private static readonly Uri Detail = new("https://running.life/pl/wydarzenie/bieg-testowy");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (FakeTimeProvider Clock, FakeRaceHandler Handler) Setup(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 3, 25, 0, TimeSpan.Zero));
        var handler = new FakeRaceHandler(clock) { Respond = respond ?? (r => RequestPath(r) == "/robots.txt" ? Text("User-agent: *\nDisallow: /pl/map/\n") : Html("<html></html>")) };
        return (clock, handler);
    }

    [Fact]
    public async Task Every_request_is_a_get_with_the_identifying_user_agent_and_accept_encoding()
    {
        var (clock, handler) = Setup();
        var client = Polite(handler, clock);

        await WithClockAsync(clock, async () =>
        {
            await client.LoadRobotsAsync(Ct);
            await client.GetAsync(Listing, null, Ct);
        });

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal(HttpMethod.Get, r.Method);
            Assert.Equal("RunnersDiaryRaceImport/1.0 (+https://github.com/stompsu/runners-diary)", r.UserAgent);
            Assert.Equal("gzip, br", r.AcceptEncoding);
            Assert.Equal("https", r.Url.Scheme);
        });
        Assert.Equal("/robots.txt", handler.Requests[0].Url.AbsolutePath);
    }

    [Fact]
    public async Task Requests_are_spaced_by_at_least_the_minimum_interval()
    {
        var (clock, handler) = Setup();
        var options = Options(o => o.MinInterval = TimeSpan.FromSeconds(1.5));
        var client = Polite(handler, clock, options);

        await WithClockAsync(clock, async () =>
        {
            await client.LoadRobotsAsync(Ct);
            await client.GetAsync(Listing, null, Ct);
            await client.GetAsync(Detail, null, Ct);
            await client.GetAsync(Listing, null, Ct);
        });

        Assert.Equal(4, handler.Requests.Count);
        for (var i = 1; i < handler.Requests.Count; i++)
        {
            Assert.True(handler.Requests[i].At - handler.Requests[i - 1].At >= TimeSpan.FromSeconds(1.5), $"Gap before request {i}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void A_minimum_interval_below_one_second_is_rejected_at_startup(int milliseconds)
    {
        var options = new RaceImportOptions { MinInterval = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Contains(options.Validate(), p => p.Contains("RaceImport:MinInterval", StringComparison.Ordinal));
        Assert.Empty(new RaceImportOptions { MinInterval = TimeSpan.FromSeconds(1) }.Validate());
        Assert.Empty(new RaceImportOptions().Validate());
    }

    [Fact]
    public async Task A_disallowed_path_aborts_the_source_and_an_allowed_one_is_fetched()
    {
        var (clock, handler) = Setup(r => RequestPath(r) == "/robots.txt" ? Text(Fixture("robots-disallow.txt")) : Html("ok"));
        var client = Polite(handler, clock);

        await WithClockAsync(clock, async () =>
        {
            await client.LoadRobotsAsync(Ct);
            Assert.True((await client.GetAsync(Listing, null, Ct)).IsOk);
            var stop = await Assert.ThrowsAsync<SourceStopException>(() => client.GetAsync(Detail, null, Ct));
            Assert.Equal(RaceScanOutcome.RobotsDisallowed, stop.Outcome);
        });

        Assert.DoesNotContain(handler.Requests, r => r.Url.AbsolutePath.StartsWith("/pl/wydarzenie/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_404_robots_file_allows_everything()
    {
        var (clock, handler) = Setup(r => RequestPath(r) == "/robots.txt" ? Status(HttpStatusCode.NotFound) : Html("ok"));
        var client = Polite(handler, clock);

        await WithClockAsync(clock, async () =>
        {
            await client.LoadRobotsAsync(Ct);
            Assert.True((await client.GetAsync(Detail, null, Ct)).IsOk);
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_5xx_robots_file_aborts_the_source(HttpStatusCode status)
    {
        var (clock, handler) = Setup(_ => Status(status));
        var client = Polite(handler, clock);

        var stop = await WithClockAsync(clock, () => Assert.ThrowsAsync<SourceStopException>(() => client.LoadRobotsAsync(Ct)));

        Assert.Equal(RaceScanOutcome.RobotsUnavailable, stop.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task An_unparsable_robots_file_aborts_the_source()
    {
        var (clock, handler) = Setup(_ => Html("<!DOCTYPE html><html><body>Welcome</body></html>"));
        var client = Polite(handler, clock);

        var stop = await WithClockAsync(clock, () => Assert.ThrowsAsync<SourceStopException>(() => client.LoadRobotsAsync(Ct)));

        Assert.Equal(RaceScanOutcome.RobotsUnavailable, stop.Outcome);
    }

    [Fact]
    public async Task A_robots_timeout_aborts_the_source()
    {
        var (clock, handler) = Setup();
        handler.Respond = _ => throw new TaskCanceledException("timeout");
        var client = Polite(handler, clock);

        var stop = await WithClockAsync(clock, () => Assert.ThrowsAsync<SourceStopException>(() => client.LoadRobotsAsync(Ct)));

        Assert.Equal(RaceScanOutcome.RobotsUnavailable, stop.Outcome);
    }

    [Fact]
    public async Task Stored_validators_are_sent_and_a_304_keeps_the_stored_facts()
    {
        var (clock, handler) = Setup(_ => Status(HttpStatusCode.NotModified));
        var client = Polite(handler, clock);

        var response = await WithClockAsync(clock, () => client.GetAsync(Detail, new ConditionalRequest("\"abc\"", "Fri, 13 Nov 2026 10:00:00 GMT"), Ct));

        Assert.Equal(PoliteOutcome.NotModified, response.Outcome);
        Assert.Equal("\"abc\"", handler.Requests.Single().IfNoneMatch);
        Assert.Equal("Fri, 13 Nov 2026 10:00:00 GMT", handler.Requests.Single().IfModifiedSince);
    }

    [Fact]
    public async Task A_304_without_a_conditional_request_is_not_trusted()
    {
        var (clock, handler) = Setup(_ => Status(HttpStatusCode.NotModified));
        var client = Polite(handler, clock);

        var response = await WithClockAsync(clock, () => client.GetAsync(Detail, null, Ct));

        Assert.Equal(PoliteOutcome.ClientError, response.Outcome);
    }

    [Fact]
    public async Task A_200_answer_reports_its_validators()
    {
        var (clock, handler) = Setup(_ =>
        {
            var response = Html("<html>x</html>");
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            response.Content.Headers.LastModified = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
            return response;
        });
        var client = Polite(handler, clock);

        var response = await WithClockAsync(clock, () => client.GetAsync(Detail, null, Ct));

        Assert.Equal("\"v1\"", response.ETag);
        Assert.Equal("Sun, 01 Nov 2026 08:00:00 GMT", response.LastModified);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_403_or_429_stops_the_source_at_once_and_is_never_retried(HttpStatusCode status)
    {
        var (clock, handler) = Setup(_ =>
        {
            var response = Status(status);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return response;
        });
        var client = Polite(handler, clock);

        var stop = await WithClockAsync(clock, () => Assert.ThrowsAsync<SourceStopException>(() => client.GetAsync(Detail, null, Ct)));

        Assert.Equal(RaceScanOutcome.StoppedByServer, stop.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Three_consecutive_5xx_answers_stop_the_source_and_a_success_resets_the_count()
    {
        var calls = 0;
        var (clock, handler) = Setup();
        handler.Respond = _ => ++calls is 3 ? Html("ok") : Status(HttpStatusCode.BadGateway);
        var client = Polite(handler, clock);

        await WithClockAsync(clock, async () =>
        {
            Assert.Equal(PoliteOutcome.ServerError, (await client.GetAsync(Detail, null, Ct)).Outcome);
            Assert.Equal(PoliteOutcome.ServerError, (await client.GetAsync(Detail, null, Ct)).Outcome);
            Assert.Equal(PoliteOutcome.Ok, (await client.GetAsync(Detail, null, Ct)).Outcome);
            Assert.Equal(PoliteOutcome.ServerError, (await client.GetAsync(Detail, null, Ct)).Outcome);
            Assert.Equal(PoliteOutcome.ServerError, (await client.GetAsync(Detail, null, Ct)).Outcome);
            var stop = await Assert.ThrowsAsync<SourceStopException>(() => client.GetAsync(Detail, null, Ct));
            Assert.Equal(RaceScanOutcome.StoppedByFailures, stop.Outcome);
        });

        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task Timeouts_count_as_failures_and_do_not_hang()
    {
        var (clock, _) = Setup();
        var client = Polite(new HangingHandler(), clock, Options(o => o.RequestTimeout = TimeSpan.FromSeconds(20)));

        var response = await WithClockAsync(clock, () => client.GetAsync(Detail, null, Ct));

        Assert.Equal(PoliteOutcome.ServerError, response.Outcome);
    }

    [Fact]
    public async Task A_same_host_redirect_is_followed_and_each_hop_is_spaced()
    {
        var (clock, handler) = Setup(r => RequestPath(r) == "/old"
            ? new HttpResponseMessage(HttpStatusCode.MovedPermanently) { Headers = { Location = new Uri("/pl/wydarzenie/new", UriKind.Relative) } }
            : Html("moved here"));
        var client = Polite(handler, clock);

        var response = await WithClockAsync(clock, () => client.GetAsync(new Uri("https://running.life/old"), null, Ct));

        Assert.Equal("moved here", response.Body);
        Assert.Equal(["/old", "/pl/wydarzenie/new"], handler.Requests.Select(r => r.Url.AbsolutePath));
        Assert.True(handler.Requests[1].At - handler.Requests[0].At >= TimeSpan.FromSeconds(1.5));
    }

    [Theory]
    [InlineData("https://elsewhere.example/page")]
    [InlineData("http://running.life/page")]
    [InlineData("https://running.life.evil.example/page")]
    [InlineData("https://running.life:8443/page")]
    public async Task A_redirect_off_the_host_or_to_http_is_refused_and_not_followed(string target)
    {
        var (clock, handler) = Setup(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(target) } });
        var client = Polite(handler, clock);

        var response = await WithClockAsync(clock, () => client.GetAsync(Detail, null, Ct));

        Assert.Equal(PoliteOutcome.Refused, response.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task The_body_is_decoded_with_the_declared_charset_and_falls_back_to_utf8()
    {
        // The test host registers the code pages itself: on Linux the runtime knows only the Unicode encodings until a provider is added.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var latin2 = System.Text.Encoding.GetEncoding("iso-8859-2");
        var (clock, handler) = Setup(r =>
        {
            var declared = RequestPath(r) == "/latin";
            var content = new ByteArrayContent(declared ? latin2.GetBytes("Żółć") : System.Text.Encoding.UTF8.GetBytes("Żółć"));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = declared ? "iso-8859-2" : (RequestPath(r) == "/bogus" ? "no-such-charset" : null) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var client = Polite(handler, clock);

        var declared = await WithClockAsync(clock, () => client.GetAsync(new Uri("https://running.life/latin"), null, Ct));
        var none = await WithClockAsync(clock, () => client.GetAsync(new Uri("https://running.life/none"), null, Ct));
        var bogus = await WithClockAsync(clock, () => client.GetAsync(new Uri("https://running.life/bogus"), null, Ct));

        Assert.Equal("Żółć", declared.Body);
        Assert.Equal("Żółć", none.Body);
        Assert.Equal("Żółć", bogus.Body);
    }

    [Fact]
    public async Task A_redirect_loop_is_cut()
    {
        var (clock, handler) = Setup(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://running.life/loop") } });
        var client = Polite(handler, clock);

        var response = await WithClockAsync(clock, () => client.GetAsync(Detail, null, Ct));

        Assert.Equal(PoliteOutcome.Refused, response.Outcome);
        Assert.True(handler.Requests.Count <= 4);
    }

    [Theory]
    [InlineData("https://elsewhere.example/pl/wydarzenie/x")]
    [InlineData("http://running.life/pl/wydarzenie/x")]
    [InlineData("https://user:pw@running.life/pl/wydarzenie/x")]
    [InlineData("https://running.life:8443/pl/wydarzenie/x")]
    public async Task A_url_outside_the_allow_list_is_never_requested(string url)
    {
        var (clock, handler) = Setup();
        var client = Polite(handler, clock);

        var response = await WithClockAsync(clock, () => client.GetAsync(new Uri(url), null, Ct));

        Assert.Equal(PoliteOutcome.Refused, response.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_body_over_the_cap_is_dropped_whether_or_not_the_length_is_declared()
    {
        var cap = 4096;
        var (clock, handler) = Setup(r => RequestPath(r) switch
        {
            "/declared" => Html(new string('x', cap + 1)),
            "/streamed" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(cap + 1) },
            _ => Html(new string('y', cap)),
        });
        var client = Polite(handler, clock, Options(o => o.MaxResponseBytes = cap));

        await WithClockAsync(clock, async () =>
        {
            Assert.Equal(PoliteOutcome.TooLarge, (await client.GetAsync(new Uri("https://running.life/declared"), null, Ct)).Outcome);
            Assert.Equal(PoliteOutcome.TooLarge, (await client.GetAsync(new Uri("https://running.life/streamed"), null, Ct)).Outcome);
            Assert.Equal(PoliteOutcome.Ok, (await client.GetAsync(new Uri("https://running.life/exact"), null, Ct)).Outcome);
        });
    }

    [Fact]
    public async Task A_missing_page_is_skipped_without_stopping_the_source()
    {
        var (clock, handler) = Setup(_ => Status(HttpStatusCode.NotFound));
        var client = Polite(handler, clock);

        await WithClockAsync(clock, async () =>
        {
            for (var i = 0; i < 5; i++)
            {
                Assert.Equal(PoliteOutcome.ClientError, (await client.GetAsync(Detail, null, Ct)).Outcome);
            }
        });
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>A body without a Content-Length header (chunked), so only the byte count can stop it.</summary>
    private sealed class UnknownLengthContent(int length) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            await stream.WriteAsync(new byte[length]);

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
