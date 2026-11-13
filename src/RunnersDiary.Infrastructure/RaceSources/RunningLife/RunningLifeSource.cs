using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Races;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Infrastructure.RaceSources.RunningLife;

/// <summary>
/// The running.life adapter (ADR 0035 §1): the Polish listing, a few pages per run, then the detail pages the budget allows.
/// Every request goes through <see cref="PoliteHttpClient"/>. A scan never touches the database; it works from the <see cref="RaceScanContext"/>.
/// <list type="bullet">
/// <item>Listing pages 1..N (N from the pagination links, capped by <c>MaxListingPages</c>). A 200 page without a single event is a layout change.</item>
/// <item>Detail pages: new races first, then races whose listing facts changed, then the stalest ones (older than <c>DetailRefreshAfter</c>),
/// at most <c>MaxDetailPages</c>. A 304 or a spent budget keeps the stored facts. A new race whose detail page was not read is deferred to a later run.</item>
/// </list>
/// </summary>
internal sealed partial class RunningLifeSource : IRaceSource
{
    private readonly HttpClient _http;
    private readonly IOptions<RaceImportOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<RunningLifeSource> _logger;

    public RunningLifeSource(HttpClient http, IOptions<RaceImportOptions> options, TimeProvider clock, ILogger<RunningLifeSource> logger)
    {
        _http = http;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public RaceSource Source => RaceSource.RunningLife;

    public string Id => RaceSourceRegistry.RunningLifeId;

    public async Task<RaceScanResult> ScanAsync(RaceScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = _options.Value;
        var polite = new PoliteHttpClient(_http, RunningLifeParser.Host, Id, options, _clock, _logger);
        var counters = new Counters();
        var items = new Dictionary<string, RunningLifeListingItem>(StringComparer.Ordinal);
        var detailed = new Dictionary<string, RaceFacts>(StringComparer.Ordinal);
        var records = new List<DetailFetchRecord>();
        var listing = new ListingState();
        var stop = RaceScanOutcome.Completed;

        try
        {
            await polite.LoadRobotsAsync(cancellationToken);

            await ReadListingAsync(polite, options, items, counters, listing, cancellationToken);
            if (listing.LayoutChanged)
            {
                return RaceScanResult.Failed(RaceScanOutcome.LayoutChanged, counters.ToStats(polite.Requests));
            }

            await ReadDetailsAsync(polite, options, context, items, detailed, records, counters, cancellationToken);
        }
        catch (SourceStopException ex)
        {
            stop = ex.Outcome;
            listing.Complete = false;
            if (ex.Outcome is RaceScanOutcome.RobotsDisallowed or RaceScanOutcome.RobotsUnavailable)
            {
                return RaceScanResult.Failed(ex.Outcome, counters.ToStats(polite.Requests));
            }
        }

        var facts = Assemble(context, items, detailed, counters);
        var outcome = stop != RaceScanOutcome.Completed ? stop : listing.Complete ? RaceScanOutcome.Completed : RaceScanOutcome.StoppedByFailures;
        return new RaceScanResult(facts, listing.Complete, outcome, counters.ToStats(polite.Requests), records);
    }

    private async Task ReadListingAsync(
        PoliteHttpClient polite, RaceImportOptions options, Dictionary<string, RunningLifeListingItem> items, Counters counters,
        ListingState state, CancellationToken cancellationToken)
    {
        var maxPage = 1;
        for (var page = 1; page <= maxPage && page <= options.MaxListingPages; page++)
        {
            var response = await polite.GetAsync(ListingUrl(page), conditional: null, cancellationToken);
            if (!response.IsOk)
            {
                counters.Skipped++;
                state.Complete = false;
                LogListingPageFailed(page, response.Outcome);
                continue;
            }

            var parsed = RunningLifeParser.ParseListing(response.Body ?? string.Empty);
            counters.ListingPages++;
            counters.MalformedJsonLd += parsed.MalformedJsonLd;
            counters.Skipped += parsed.Rejected;
            if (parsed.MalformedJsonLd > 0)
            {
                LogMalformedJsonLd(page, parsed.MalformedJsonLd);
            }

            if (parsed.EventCount == 0 || parsed.Items.Count + parsed.NonRunning == 0)
            {
                LogLayoutChanged(page);
                state.LayoutChanged = true;
                return;
            }

            maxPage = Math.Max(maxPage, parsed.MaxPage);
            foreach (var item in parsed.Items)
            {
                items.TryAdd(item.ExternalId, item);
            }
        }

        if (maxPage > options.MaxListingPages)
        {
            state.Complete = false;
            LogListingBudget(maxPage, options.MaxListingPages);
        }
    }

    private async Task ReadDetailsAsync(
        PoliteHttpClient polite, RaceImportOptions options, RaceScanContext context, Dictionary<string, RunningLifeListingItem> items,
        Dictionary<string, RaceFacts> detailed, List<DetailFetchRecord> records, Counters counters, CancellationToken cancellationToken)
    {
        var newOnes = new List<RunningLifeListingItem>();
        var changed = new List<RunningLifeListingItem>();
        var stale = new List<(RunningLifeListingItem Item, DateTime FetchedUtc)>();
        var refreshBefore = context.Now.UtcDateTime - options.DetailRefreshAfter;
        foreach (var item in items.Values)
        {
            if (!context.Known.TryGetValue(item.ExternalId, out var known))
            {
                newOnes.Add(item);
            }
            else if (RunningLifeParser.ListingChanged(known.Facts, item))
            {
                changed.Add(item);
            }
            else if (known.DetailFetchedUtc is not { } fetched || fetched <= refreshBefore)
            {
                stale.Add((item, known.DetailFetchedUtc ?? DateTime.MinValue));
            }
        }

        var queue = newOnes.Concat(changed).Concat(stale.OrderBy(s => s.FetchedUtc).Select(s => s.Item)).Take(options.MaxDetailPages).ToList();
        foreach (var item in queue)
        {
            context.Known.TryGetValue(item.ExternalId, out var known);
            var conditional = known is not null && (known.DetailETag is not null || known.DetailLastModified is not null)
                ? new ConditionalRequest(known.DetailETag, known.DetailLastModified)
                : null;

            var response = await polite.GetAsync(item.DetailUrl, conditional, cancellationToken);
            switch (response.Outcome)
            {
                case PoliteOutcome.Ok:
                    var page = RunningLifeParser.ParseDetail(response.Body ?? string.Empty);
                    counters.DetailPages++;
                    counters.MalformedJsonLd += page.MalformedJsonLd;
                    if (page.IsEmpty && known is not null)
                    {
                        // Nothing recognisable on a page that used to carry facts: keep what is stored (a layout change must not wipe the catalogue).
                        counters.Skipped++;
                        LogEmptyDetail();
                        break;
                    }

                    detailed[item.ExternalId] = RunningLifeParser.ToFacts(item, page);
                    records.Add(new DetailFetchRecord(item.ExternalId, response.ETag, response.LastModified));
                    break;
                case PoliteOutcome.NotModified when known is not null:
                    counters.NotModified++;
                    records.Add(new DetailFetchRecord(item.ExternalId, known.DetailETag, known.DetailLastModified));
                    break;
                default:
                    counters.Skipped++;
                    break;
            }
        }
    }

    private static List<RaceFacts> Assemble(
        RaceScanContext context, Dictionary<string, RunningLifeListingItem> items, Dictionary<string, RaceFacts> detailed, Counters counters)
    {
        var facts = new List<RaceFacts>(items.Count);
        foreach (var (id, item) in items)
        {
            if (detailed.TryGetValue(id, out var fresh))
            {
                facts.Add(fresh);
            }
            else if (context.Known.TryGetValue(id, out var known))
            {
                facts.Add(RunningLifeParser.WithListing(known.Facts, item));
            }
            else
            {
                counters.Deferred++;
            }
        }

        return facts;
    }

    private static Uri ListingUrl(int page) => new($"https://{RunningLifeParser.Host}/kalendarz-biegow/polska?page={page}");

    private sealed class ListingState
    {
        public bool Complete { get; set; } = true;

        public bool LayoutChanged { get; set; }
    }

    private sealed class Counters
    {
        public int ListingPages { get; set; }

        public int DetailPages { get; set; }

        public int NotModified { get; set; }

        public int Skipped { get; set; }

        public int Deferred { get; set; }

        public int MalformedJsonLd { get; set; }

        public RaceFetchStats ToStats(int requests) => new()
        {
            ListingPages = ListingPages,
            DetailPages = DetailPages,
            NotModified = NotModified,
            Requests = requests,
            Skipped = Skipped,
            Deferred = Deferred,
            MalformedJsonLd = MalformedJsonLd,
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "running.life: listing page {Page} was not read ({Outcome}); the scan is incomplete.")]
    private partial void LogListingPageFailed(int page, PoliteOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "running.life: {Count} malformed JSON-LD block(s) skipped on listing page {Page}.")]
    private partial void LogMalformedJsonLd(int page, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "running.life: listing page {Page} returned 200 without a single event; the page layout has probably changed. Stopping the source.")]
    private partial void LogLayoutChanged(int page);

    [LoggerMessage(Level = LogLevel.Warning, Message = "running.life: the listing has {Pages} pages but at most {Budget} are read per run; the scan is incomplete.")]
    private partial void LogListingBudget(int pages, int budget);

    [LoggerMessage(Level = LogLevel.Warning, Message = "running.life: a detail page without any recognisable fact was ignored; stored facts are kept.")]
    private partial void LogEmptyDetail();
}
