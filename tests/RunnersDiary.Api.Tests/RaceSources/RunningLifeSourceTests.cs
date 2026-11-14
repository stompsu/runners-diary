using System.Net;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Domain.Races;
using RunnersDiary.Infrastructure.RaceSources;
using RunnersDiary.Infrastructure.RaceSources.RunningLife;
using static RunnersDiary.Api.Tests.RaceSources.RaceSourceTestSupport;

namespace RunnersDiary.Api.Tests.RaceSources;

/// <summary>The running.life adapter end to end on a scripted server: budgets, 304, stop rules, layout change (ADR 0035 §4, AC10). No live request is ever made.</summary>
public sealed class RunningLifeSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 3, 25, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 13);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string Id, string Name, string Date)[] Listed =
    [
        ("alpha", "Alpha Run", "2027-03-01"),
        ("beta", "Beta Run", "2027-03-02"),
        ("gamma", "Gamma Run", "2027-03-03"),
        ("delta", "Delta Run", "2027-03-04"),
    ];

    private static readonly IReadOnlyDictionary<string, KnownRace> NothingKnown = new Dictionary<string, KnownRace>();

    private static RaceScanContext Context(IReadOnlyDictionary<string, KnownRace>? known = null) => new(known ?? NothingKnown, Today, Now);

    private static (FakeTimeProvider Clock, FakeRaceHandler Handler) Server(Func<HttpRequestMessage, HttpResponseMessage> pages)
    {
        var clock = new FakeTimeProvider(Now);
        var handler = new FakeRaceHandler(clock)
        {
            Respond = r => RequestPath(r) == "/robots.txt" ? Text(Fixture("robots-allow.txt")) : pages(r),
        };
        return (clock, handler);
    }

    private static HttpResponseMessage ThreePages(HttpRequestMessage r) => (RequestPath(r), r.RequestUri!.Query) switch
    {
        ("/kalendarz-biegow/polska", "?page=1") => Html(ListingHtml(3, ("alpha", "Alpha Run", "2027-03-01"), ("beta", "Beta Run", "2027-03-02"))),
        ("/kalendarz-biegow/polska", "?page=2") => Html(ListingHtml(3, ("gamma", "Gamma Run", "2027-03-03"))),
        ("/kalendarz-biegow/polska", "?page=3") => Html(ListingHtml(3, ("delta", "Delta Run", "2027-03-04"))),
        ("/pl/wydarzenie/alpha", _) => Html(DetailHtml("10 km|5 km")),
        ("/pl/wydarzenie/beta", _) => Html(DetailHtml("Półmaraton")),
        ("/pl/wydarzenie/gamma", _) => Html(DetailHtml("Maraton")),
        ("/pl/wydarzenie/delta", _) => Html(DetailHtml("Trail 50 km")),
        _ => Status(HttpStatusCode.NotFound),
    };

    private static KnownRace Known(string id, DateTime? fetched, string? etag = null, string? modified = null, Action<RaceFacts>? check = null)
    {
        var facts = new RaceFacts(
            RaceSource.RunningLife, id, id + " stored name", new DateOnly(2027, 3, 1), null, "Gliwice", null, "PL", "PL-24", RaceKind.Road, null, [15_000],
            null, "https://zapisy.example/stored", "https://club.example/stored", $"https://running.life/pl/wydarzenie/{id}", null, null, null, null);
        check?.Invoke(facts);
        return new KnownRace(Guid.NewGuid(), facts, fetched, etag, modified);
    }

    [Fact]
    public async Task A_first_run_reads_every_listing_page_then_each_new_races_detail_page_and_is_complete()
    {
        var (clock, handler) = Server(ThreePages);
        var source = Source(handler, clock);

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.Equal(RaceScanOutcome.Completed, result.Outcome);
        Assert.True(result.Complete);
        Assert.Equal(4, result.Facts.Count);
        Assert.Equal(3, result.Stats.ListingPages);
        Assert.Equal(4, result.Stats.DetailPages);
        Assert.Equal(0, result.Stats.Deferred);
        Assert.Equal(1 + 3 + 4, result.Stats.Requests); // robots + listing + details
        Assert.Equal(4, result.DetailFetches.Count);

        var alpha = result.Facts.Single(f => f.ExternalId == "alpha");
        Assert.Equal([5_000, 10_000], alpha.DistancesMeters);
        Assert.Equal("https://zapisy.example/x", alpha.SignupUrl);
        Assert.Equal(RaceSource.RunningLife, alpha.Source);
        Assert.Equal("PL-24", alpha.RegionCode);
        Assert.Equal(21_097.5, Assert.Single(result.Facts.Single(f => f.ExternalId == "beta").DistancesMeters));

        Assert.Equal("/robots.txt", handler.Requests[0].Url.AbsolutePath); // robots first
        Assert.All(handler.Requests, r => Assert.Equal(Host, r.Url.Host));
        Assert.All(handler.Requests, r => Assert.Equal("RunnersDiaryRaceImport/1.0 (+https://github.com/stompsu/runners-diary)", r.UserAgent));
    }

    [Fact]
    public async Task Known_races_with_fresh_detail_pages_and_unchanged_listing_facts_cost_no_detail_request()
    {
        var (clock, handler) = Server(ThreePages);
        var source = Source(handler, clock);
        var known = new Dictionary<string, KnownRace>();
        foreach (var (id, name, date) in Listed)
        {
            var stored = Known(id, Now.UtcDateTime.AddDays(-3));
            // Store exactly what the listing says, so nothing counts as changed.
            var listing = RunningLifeParser.ParseListing(ListingHtml(1, (id, name, date))).Items.Single();
            known[id] = stored with { Facts = RunningLifeParser.WithListing(stored.Facts, listing) };
        }

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(known), Ct));

        Assert.Equal(RaceScanOutcome.Completed, result.Outcome);
        Assert.Equal(0, result.Stats.DetailPages);
        Assert.DoesNotContain(handler.Requests, r => r.Url.AbsolutePath.StartsWith("/pl/wydarzenie/", StringComparison.Ordinal));
        Assert.Equal(4, result.Facts.Count);
        Assert.All(result.Facts, f => Assert.Equal([15_000], f.DistancesMeters)); // stored detail facts survive
        Assert.All(result.Facts, f => Assert.Equal("https://zapisy.example/stored", f.SignupUrl));
        Assert.Empty(result.DetailFetches);
    }

    [Fact]
    public async Task A_stale_detail_page_is_requested_conditionally_and_a_304_keeps_the_stored_facts()
    {
        var (clock, handler) = Server(r => RequestPath(r) switch
        {
            "/kalendarz-biegow/polska" => Html(ListingHtml(1, ("alpha", "Alpha Run", "2027-03-01"))),
            "/pl/wydarzenie/alpha" => Status(HttpStatusCode.NotModified),
            _ => Status(HttpStatusCode.NotFound),
        });
        var source = Source(handler, clock);
        var listing = RunningLifeParser.ParseListing(ListingHtml(1, ("alpha", "Alpha Run", "2027-03-01"))).Items.Single();
        var stored = Known("alpha", Now.UtcDateTime.AddDays(-29), "\"etag-1\"", "Fri, 16 Oct 2026 10:00:00 GMT");
        var known = new Dictionary<string, KnownRace> { ["alpha"] = stored with { Facts = RunningLifeParser.WithListing(stored.Facts, listing) } };

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(known), Ct));

        var detail = handler.Requests.Single(r => r.Url.AbsolutePath == "/pl/wydarzenie/alpha");
        Assert.Equal("\"etag-1\"", detail.IfNoneMatch);
        Assert.Equal("Fri, 16 Oct 2026 10:00:00 GMT", detail.IfModifiedSince);
        Assert.Equal(1, result.Stats.NotModified);
        Assert.Equal(0, result.Stats.DetailPages);
        var fact = Assert.Single(result.Facts);
        Assert.Equal([15_000], fact.DistancesMeters);
        var record = Assert.Single(result.DetailFetches);
        Assert.Equal(("alpha", "\"etag-1\""), (record.ExternalId, record.ETag));
    }

    [Fact]
    public async Task A_changed_listing_fact_triggers_a_detail_request_even_when_the_page_is_fresh()
    {
        var (clock, handler) = Server(r => RequestPath(r) switch
        {
            "/kalendarz-biegow/polska" => Html(ListingHtml(1, ("alpha", "Alpha Run Renamed", "2027-03-08"))),
            "/pl/wydarzenie/alpha" => Html(DetailHtml("10 km")),
            _ => Status(HttpStatusCode.NotFound),
        });
        var source = Source(handler, clock);
        var known = new Dictionary<string, KnownRace> { ["alpha"] = Known("alpha", Now.UtcDateTime.AddDays(-1)) };

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(known), Ct));

        Assert.Equal(1, result.Stats.DetailPages);
        var fact = Assert.Single(result.Facts);
        Assert.Equal("Alpha Run Renamed", fact.Name);
        Assert.Equal(new DateOnly(2027, 3, 8), fact.Date);
        Assert.Equal([10_000], fact.DistancesMeters);
    }

    [Fact]
    public async Task The_detail_budget_is_spent_on_new_races_first_and_the_rest_are_deferred_not_inserted()
    {
        var (clock, handler) = Server(ThreePages);
        var source = Source(handler, clock, Options(o => o.MaxDetailPages = 1));

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.Equal(1, result.Stats.DetailPages);
        Assert.Equal(3, result.Stats.Deferred);
        Assert.Single(result.Facts);
        Assert.True(result.Complete, "The listing was complete, so the merger may still count missing races.");
        Assert.Equal(1, handler.Requests.Count(r => r.Url.AbsolutePath.StartsWith("/pl/wydarzenie/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_listing_page_budget_makes_the_scan_incomplete()
    {
        var (clock, handler) = Server(ThreePages);
        var source = Source(handler, clock, Options(o => o.MaxListingPages = 2));

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.False(result.Complete);
        Assert.Equal(RaceScanOutcome.StoppedByFailures, result.Outcome);
        Assert.Equal(2, result.Stats.ListingPages);
        Assert.DoesNotContain(handler.Requests, r => r.Url.Query == "?page=3");
        Assert.False(result.IsFailure);
    }

    [Fact]
    public async Task A_200_listing_page_without_events_is_a_layout_change_and_nothing_else_is_requested()
    {
        var (clock, handler) = Server(_ => Html(Fixture("listing-empty-layout.html")));
        var source = Source(handler, clock);

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.Equal(RaceScanOutcome.LayoutChanged, result.Outcome);
        Assert.True(result.IsFailure);
        Assert.False(result.Complete);
        Assert.Empty(result.Facts);
        Assert.Equal(2, handler.Requests.Count); // robots + the first listing page
    }

    [Fact]
    public async Task A_layout_change_on_a_later_listing_page_also_stops_the_source()
    {
        var (clock, handler) = Server(r => r.RequestUri!.Query == "?page=2" ? Html(Fixture("listing-empty-layout.html")) : ThreePages(r));
        var source = Source(handler, clock);

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.Equal(RaceScanOutcome.LayoutChanged, result.Outcome);
        Assert.Empty(result.Facts);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_403_or_429_midway_stops_the_source_with_a_partial_incomplete_result_that_is_not_a_failure(HttpStatusCode status)
    {
        var (clock, handler) = Server(r => r.RequestUri!.Query == "?page=2" ? Status(status) : ThreePages(r));
        var source = Source(handler, clock);
        var known = new Dictionary<string, KnownRace> { ["alpha"] = Known("alpha", Now.UtcDateTime.AddDays(-1)) };

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(known), Ct));

        Assert.Equal(RaceScanOutcome.StoppedByServer, result.Outcome);
        Assert.False(result.IsFailure);
        Assert.False(result.Complete);
        Assert.Equal(["alpha"], result.Facts.Select(f => f.ExternalId)); // new "beta" has no detail yet: deferred; known "alpha" kept
        Assert.Equal(1, result.Stats.Deferred);
        Assert.Equal(1 + 2, handler.Requests.Count); // robots, page 1, page 2 (refused); no retry and nothing after
    }

    [Fact]
    public async Task A_failing_listing_page_makes_the_scan_incomplete_but_the_other_pages_are_used()
    {
        var (clock, handler) = Server(r => r.RequestUri!.Query == "?page=2" ? Status(HttpStatusCode.ServiceUnavailable) : ThreePages(r));
        var source = Source(handler, clock);

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.False(result.Complete);
        Assert.Equal(RaceScanOutcome.StoppedByFailures, result.Outcome);
        Assert.Equal(3, result.Facts.Count); // alpha, beta, delta
    }

    [Fact]
    public async Task Three_failures_in_a_row_stop_the_source()
    {
        var (clock, handler) = Server(r => RequestPath(r).StartsWith("/pl/wydarzenie/", StringComparison.Ordinal) ? Status(HttpStatusCode.BadGateway) : ThreePages(r));
        var source = Source(handler, clock);

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.Equal(RaceScanOutcome.StoppedByFailures, result.Outcome);
        Assert.False(result.IsFailure);
        Assert.Equal(1 + 3 + 3, handler.Requests.Count); // robots, three listing pages, three failing detail pages: no fourth
        Assert.Equal(4, result.Stats.Deferred);
    }

    [Fact]
    public async Task A_disallowing_robots_file_aborts_before_any_page_is_requested()
    {
        var clock = new FakeTimeProvider(Now);
        var handler = new FakeRaceHandler(clock)
        {
            Respond = r => RequestPath(r) == "/robots.txt" ? Text("User-agent: *\nDisallow: /kalendarz-biegow/\n") : ThreePages(r),
        };
        var source = Source(handler, clock);

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.Equal(RaceScanOutcome.RobotsDisallowed, result.Outcome);
        Assert.True(result.IsFailure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_503_robots_file_aborts_the_source_as_a_failure()
    {
        var clock = new FakeTimeProvider(Now);
        var handler = new FakeRaceHandler(clock) { Respond = _ => Status(HttpStatusCode.ServiceUnavailable) };
        var source = Source(handler, clock);

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.Equal(RaceScanOutcome.RobotsUnavailable, result.Outcome);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task A_detail_url_on_a_foreign_host_is_never_requested()
    {
        var (clock, handler) = Server(r => RequestPath(r) == "/kalendarz-biegow/polska" ? Html(Fixture("listing-p1.html")) : Html(DetailHtml()));
        var source = Source(handler, clock, Options(o => o.MaxListingPages = 1));

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.All(handler.Requests, r => Assert.Equal(Host, r.Url.Host));
        Assert.DoesNotContain(handler.Requests, r => r.Url.AbsolutePath.Contains("phish", StringComparison.Ordinal));
        Assert.Equal(3, result.Facts.Count);
        Assert.Equal(3, result.Stats.Skipped); // the foreign host, the missing date and the foreign country
    }

    [Fact]
    public async Task One_invalid_listing_item_is_counted_and_the_others_still_import_without_an_incomplete_scan()
    {
        var (clock, handler) = Server(r => RequestPath(r) == "/kalendarz-biegow/polska" ? Html(Fixture("listing-one-invalid.html")) : Html(DetailHtml()));
        var source = Source(handler, clock);

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.Equal(RaceScanOutcome.Completed, result.Outcome);
        Assert.True(result.Complete);
        Assert.Equal(["alpha", "gamma"], result.Facts.Select(f => f.ExternalId).Order());
        Assert.Equal(1, result.Stats.Rejected);
        Assert.DoesNotContain(handler.Requests, r => r.Url.AbsolutePath.Contains("far-future", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_listing_where_every_item_is_invalid_is_not_a_layout_change()
    {
        var (clock, handler) = Server(r => Html(ListingHtml(1, ("a", new string('N', 201), "2027-03-01"), ("b", "B Run", "2101-01-01"))));
        var source = Source(handler, clock);

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.Equal(RaceScanOutcome.Completed, result.Outcome);
        Assert.False(result.IsFailure);
        Assert.Empty(result.Facts);
        Assert.Equal(2, result.Stats.Rejected);
    }

    [Fact]
    public async Task A_detail_page_whose_link_is_too_long_still_yields_the_race_without_that_link()
    {
        var (clock, handler) = Server(r => RequestPath(r) switch
        {
            "/kalendarz-biegow/polska" => Html(ListingHtml(1, ("alpha", "Alpha Run", "2027-03-08"))),
            _ => Html(DetailHtml(website: "https://club.example/" + new string('é', 1500))),
        });
        var source = Source(handler, clock);

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        var fact = Assert.Single(result.Facts);
        Assert.Null(fact.EventUrl);
        Assert.Equal(RaceScanOutcome.Completed, result.Outcome);
    }

    [Fact]
    public async Task A_detail_page_that_lost_all_its_facts_does_not_wipe_the_stored_ones()
    {
        var (clock, handler) = Server(r => RequestPath(r) switch
        {
            "/kalendarz-biegow/polska" => Html(ListingHtml(1, ("alpha", "Alpha Run", "2027-03-08"))),
            "/pl/wydarzenie/alpha" => Html("<html><body><p>new layout</p></body></html>"),
            _ => Status(HttpStatusCode.NotFound),
        });
        var source = Source(handler, clock);
        var known = new Dictionary<string, KnownRace> { ["alpha"] = Known("alpha", Now.UtcDateTime.AddDays(-40)) };

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(known), Ct));

        var fact = Assert.Single(result.Facts);
        Assert.Equal([15_000], fact.DistancesMeters);
        Assert.Equal("https://zapisy.example/stored", fact.SignupUrl);
        Assert.Empty(result.DetailFetches);
    }

    [Fact]
    public async Task Malformed_json_ld_on_a_listing_page_is_counted_and_the_good_events_are_kept()
    {
        var (clock, handler) = Server(r => RequestPath(r) == "/kalendarz-biegow/polska" ? Html(Fixture("jsonld-malformed.html")) : Html(DetailHtml()));
        var source = Source(handler, clock);

        var result = await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        Assert.Equal(1, result.Stats.MalformedJsonLd);
        Assert.Equal("bieg-ocalaly", Assert.Single(result.Facts).ExternalId);
        Assert.Equal(RaceScanOutcome.Completed, result.Outcome);
    }

    [Fact]
    public async Task Requests_to_the_source_are_spaced_by_the_minimum_interval()
    {
        var (clock, handler) = Server(ThreePages);
        var source = Source(handler, clock);

        await WithClockAsync(clock, () => source.ScanAsync(Context(), Ct));

        for (var i = 1; i < handler.Requests.Count; i++)
        {
            Assert.True(handler.Requests[i].At - handler.Requests[i - 1].At >= TimeSpan.FromSeconds(1.5));
        }
    }
}
