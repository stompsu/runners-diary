using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Infrastructure.RaceSources.RunningLife;

/// <summary>One event of a listing page's JSON-LD <c>ItemList</c>. The schema.org description is dropped here and never reaches <see cref="RaceFacts"/> (ADR 0035 §2).</summary>
internal sealed record RunningLifeListingItem(
    string ExternalId,
    Uri DetailUrl,
    string Name,
    DateOnly Date,
    string? LocationName,
    string? RegionCode,
    string CountryCode,
    GeoPoint? Point)
{
    /// <summary>The kind read from the listing card's type anchor (label text first, class as fallback); null when the card has none.</summary>
    public RaceKind? CardKind { get; init; }
}

/// <param name="EventCount">Events found in the page's <c>ItemList</c> before validation; zero on a 200 page means the layout changed.</param>
/// <param name="MaxPage">Highest <c>page=N</c> found in the page's links (1 when there is no pagination).</param>
/// <param name="Rejected">Events dropped by validation (foreign host, bad URL, no date, unknown country).</param>
internal sealed record RunningLifeListingPage(IReadOnlyList<RunningLifeListingItem> Items, int EventCount, int MaxPage, int Rejected, int MalformedJsonLd)
{
    /// <summary>Cards of non-running sports (triathlon, duathlon, aquathlon, fitness, multisport): never races.</summary>
    public int NonRunning { get; init; }

    /// <summary>Items that were readable but whose facts break a domain rule (already part of <see cref="Rejected"/>); they say nothing about the page layout.</summary>
    public int Invalid { get; init; }
}

internal sealed record RunningLifeDetailPage(
    IReadOnlyList<double> Distances,
    string? TypeLabel,
    IReadOnlyList<string> ChipTexts,
    string? WebsiteUrl,
    string? SignupUrl,
    int MalformedJsonLd)
{
    /// <summary>A page with none of the facts the detail page exists for (distances, links); a changed layout looks like this, so stored facts are kept.</summary>
    public bool IsEmpty => Distances.Count == 0 && WebsiteUrl is null && SignupUrl is null && TypeLabel is null;
}

/// <summary>
/// Pure HTML-to-facts parsing for running.life (ADR 0035 §6), testable without HTTP. HTML is parsed with AngleSharp without any loader (no scripts,
/// no CSS, no network); JSON-LD with <see cref="JsonLd"/>. Selectors were pinned on 2026-11-13 against one listing page and two detail pages:
/// <list type="bullet">
/// <item>listing: <c>script[type="application/ld+json"]</c> holding an <c>ItemList</c> of <c>SportsEvent</c>; pagination links <c>?page=N</c>;</item>
/// <item>detail: distance chips <c>span.rounded-full</c> (outside <c>.event-card-row</c>, the "nearby events" cards), the organiser's site
/// <c>a[data-out="website"]</c> and the signup link <c>a[data-out="registration"]</c>.</item>
/// </list>
/// </summary>
internal static partial class RunningLifeParser
{
    public const string Host = "running.life";
    public const string DetailPathPrefix = "/pl/wydarzenie/";

    private const int MaxPageNumber = 1000;

    public static RunningLifeListingPage ParseListing(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        using var document = new HtmlParser().ParseDocument(html);
        var (objects, malformed) = JsonLd.Extract(document);

        var cards = ReadCards(document);
        var items = new List<RunningLifeListingItem>();
        var eventCount = 0;
        var rejected = 0;
        var nonRunning = 0;
        var invalid = 0;
        foreach (var list in objects.Where(o => JsonLd.HasType(o, "ItemList")))
        {
            if (!list.TryGetProperty("itemListElement", out var elements) || elements.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var element in elements.EnumerateArray())
            {
                var evt = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("item", out var inner) ? inner : element;
                if (!JsonLd.HasType(evt, "SportsEvent"))
                {
                    continue;
                }

                eventCount++;
                if (TryReadItem(evt) is { } item)
                {
                    // One item that cannot be stored (over-long name, date out of range, over-long link) is dropped on its own; it must not fail the source.
                    if (!IsStorable(ToFacts(item, null)))
                    {
                        rejected++;
                        invalid++;
                        continue;
                    }

                    if (cards.TryGetValue(item.ExternalId, out var card))
                    {
                        if (card.NonRunning)
                        {
                            nonRunning++;
                            continue;
                        }

                        item = item with { CardKind = card.Kind };
                    }

                    items.Add(item);
                }
                else
                {
                    rejected++;
                }
            }
        }

        return new RunningLifeListingPage(items, eventCount, ReadMaxPage(document), rejected, malformed) { NonRunning = nonRunning, Invalid = invalid };
    }

    public static RunningLifeDetailPage ParseDetail(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        using var document = new HtmlParser().ParseDocument(html);
        var (_, malformed) = JsonLd.Extract(document);

        var chips = document.QuerySelectorAll("span.rounded-full")
            .Where(e => e.Closest(".event-card-row") is null)
            .Select(e => e.TextContent.Trim())
            .Where(t => t.Length is > 0 and <= 60)
            .ToList();
        var distances = RaceNormalizer.ParseDistances(chips);
        var typeLabel = document.QuerySelector("[data-race-type], .race-type")?.TextContent.Trim();

        return new RunningLifeDetailPage(
            distances,
            string.IsNullOrEmpty(typeLabel) ? null : typeLabel,
            chips,
            LinkOf(document, "website"),
            LinkOf(document, "registration"),
            malformed);
    }

    /// <summary>Whether the facts pass the domain's normalisation and validation (the same rules <c>Race.Create</c> enforces).</summary>
    internal static bool IsStorable(RaceFacts facts) => RaceFacts.TryNormalize(facts, out _, out _);

    /// <summary>Facts from a listing item and the detail page (null: listing-only values, kind <see cref="RaceKind.Other"/>).</summary>
    public static RaceFacts ToFacts(RunningLifeListingItem item, RunningLifeDetailPage? detail)
    {
        ArgumentNullException.ThrowIfNull(item);
        var kind = KindOf(item, detail);
        return new RaceFacts(
            RaceSource.RunningLife,
            item.ExternalId,
            item.Name,
            item.Date,
            StartTime: null,
            item.LocationName,
            item.Point,
            item.CountryCode,
            item.RegionCode,
            kind,
            SurfaceOf(kind),
            detail?.Distances ?? [],
            ElevationGainMeters: null,
            SignupUrl: detail?.SignupUrl,
            EventUrl: detail?.WebsiteUrl,
            SourceUrl: SourceUrlOf(item),
            PriceAmount: null,
            PriceCurrency: null,
            SignupDeadline: null,
            OrganizerName: null);
    }

    /// <summary>Stored detail facts (kind, distances, links) with the current listing values on top: used after a 304 or when the detail budget is spent.</summary>
    public static RaceFacts WithListing(RaceFacts stored, RunningLifeListingItem item)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(item);
        return stored with
        {
            Name = item.Name,
            Date = item.Date,
            LocationName = item.LocationName,
            Point = item.Point,
            CountryCode = item.CountryCode,
            RegionCode = item.RegionCode,
            SourceUrl = SourceUrlOf(item),
            Kind = item.CardKind ?? stored.Kind,
            Surface = item.CardKind is { } k ? SurfaceOf(k) : stored.Surface,
        };
    }

    /// <summary>Whether the listing values differ from the stored ones (then the detail page is read again).</summary>
    public static bool ListingChanged(RaceFacts stored, RunningLifeListingItem item)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(item);
        var current = RaceFactsHasher.CanonicalJsonString(WithListing(stored, item).Normalized());
        return !string.Equals(current, RaceFactsHasher.CanonicalJsonString(stored), StringComparison.Ordinal);
    }

    internal static string SourceUrlOf(RunningLifeListingItem item) => item.DetailUrl.GetLeftPart(UriPartial.Path);

    private static RunningLifeListingItem? TryReadItem(JsonElement evt)
    {
        var name = JsonLd.GetString(evt, "name");
        var urlText = JsonLd.GetString(evt, "url");
        var start = JsonLd.GetString(evt, "startDate");
        if (string.IsNullOrWhiteSpace(name) || urlText is null || start is null || start.Length < 10)
        {
            return null;
        }

        if (!DateOnly.TryParseExact(start.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        // The allow-list: https on the source's own host and under the event path, otherwise the event is dropped (no SSRF via source content).
        if (!TryReadDetailUrl(urlText, out var detailUrl, out var externalId))
        {
            return null;
        }

        var location = JsonLd.GetObject(evt, "location");
        var address = location is { } l ? JsonLd.GetObject(l, "address") : null;
        var locality = address is { } a ? JsonLd.GetString(a, "addressLocality") : null;
        var placeName = location is { } l2 ? JsonLd.GetString(l2, "name") : null;
        var country = address is { } a2 ? CountryCodeOf(JsonLd.GetString(a2, "addressCountry")) : "PL";
        if (country is null)
        {
            return null;
        }

        var region = address is { } a3 ? RaceNormalizer.ParseRegionCode(JsonLd.GetString(a3, "addressRegion")) : null;

        GeoPoint? point = null;
        if (location is { } l3 && JsonLd.GetObject(l3, "geo") is { } geo
            && JsonLd.GetDouble(geo, "latitude") is { } lat && JsonLd.GetDouble(geo, "longitude") is { } lon && GeoPoint.IsValid(lat, lon))
        {
            point = new GeoPoint(lat, lon);
        }

        return new RunningLifeListingItem(externalId, detailUrl, name, date, string.IsNullOrWhiteSpace(locality) ? placeName : locality, region, country, point);
    }

    /// <summary>The path after <see cref="DetailPathPrefix"/> is the external id (series: <c>slug/yyyy-mm-dd</c>).</summary>
    internal static bool TryReadDetailUrl(string text, out Uri url, out string externalId)
    {
        url = null!;
        externalId = string.Empty;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.Equals(parsed.Host, Host, StringComparison.OrdinalIgnoreCase)
            || !parsed.IsDefaultPort)
        {
            return false;
        }

        var path = parsed.AbsolutePath;
        if (!path.StartsWith(DetailPathPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var id = path[DetailPathPrefix.Length..].Trim('/');
        if (id.Length is 0 or > RaceFacts.ExternalIdMaxLength || !ExternalIdPattern().IsMatch(id) || id.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        url = new Uri($"https://{Host}{DetailPathPrefix}{id}");
        externalId = id;
        return true;
    }

    private static string? CountryCodeOf(string? text)
    {
        var folded = (text ?? string.Empty).Trim().ToUpperInvariant();
        return folded switch
        {
            "" or "POLSKA" or "POLAND" or "PL" => "PL",
            _ => null,
        };
    }

    private static int ReadMaxPage(IDocument document)
    {
        var max = 1;
        foreach (var link in document.QuerySelectorAll("a[href]"))
        {
            var href = link.GetAttribute("href");
            if (href is null || href.Length > 512)
            {
                continue;
            }

            var match = PageParameter().Match(href);
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page <= MaxPageNumber)
            {
                max = Math.Max(max, page);
            }
        }

        return max;
    }

    private static string? LinkOf(IDocument document, string kind)
    {
        foreach (var link in document.QuerySelectorAll($"a[data-out=\"{kind}\"]"))
        {
            var href = link.GetAttribute("href")?.Trim();
            if (href is { Length: > 0 and <= RaceFacts.UrlMaxLength }
                && Uri.TryCreate(href, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && string.IsNullOrEmpty(uri.UserInfo)
                && uri.AbsoluteUri.Length <= RaceFacts.UrlMaxLength)
            {
                return uri.AbsoluteUri;
            }
        }

        return null;
    }

    /// <summary>The type label when the page has one, else the name and chips ("Ultra", "trail", "górski"); unknown stays <see cref="RaceKind.Other"/>.</summary>
    private static RaceKind KindOf(RunningLifeListingItem item, RunningLifeDetailPage? detail)
    {
        // A type label on the detail page overrides the listing card; the card beats guessing from the name.
        if (detail?.TypeLabel is { } label && RaceNormalizer.ParseKind(label) is var fromLabel && fromLabel != RaceKind.Other)
        {
            return fromLabel;
        }

        return item.CardKind ?? RaceNormalizer.ParseKind(item.Name + " " + string.Join(' ', detail?.ChipTexts ?? []));
    }

    private sealed record Card(RaceKind? Kind, bool NonRunning);

    private static readonly string[] NonRunningMarkers = ["triathlon", "triatlon", "duathlon", "duatlon", "aquathlon", "akwatlon", "multisport", "fitness"];

    /// <summary>
    /// The type anchor of every listing card (<c>.event-card-distances a[class^="label-"]</c>, not <c>label-distance</c>), keyed by the event's external id.
    /// The label text decides first, the class suffix is the fallback.
    /// </summary>
    private static Dictionary<string, Card> ReadCards(IDocument document)
    {
        var cards = new Dictionary<string, Card>(StringComparer.Ordinal);
        foreach (var anchor in document.QuerySelectorAll(".event-card-row .event-card-distances a[class^=\"label-\"]"))
        {
            var cls = anchor.ClassList.FirstOrDefault(c => c.StartsWith("label-", StringComparison.Ordinal)) ?? string.Empty;
            var href = anchor.GetAttribute("href");
            if (cls == "label-distance" || href is null || !TryReadDetailUrl(href, out _, out var id))
            {
                continue;
            }

            var label = anchor.TextContent.Trim();
            var folded = (label + " " + cls).ToLowerInvariant();
            if (NonRunningMarkers.Any(m => folded.Contains(m, StringComparison.Ordinal)))
            {
                cards.TryAdd(id, new Card(null, true));
                continue;
            }

            var kind = RaceNormalizer.ParseKind(label);
            if (kind == RaceKind.Other)
            {
                kind = RaceNormalizer.ParseKind(cls["label-".Length..]);
            }

            cards.TryAdd(id, new Card(kind == RaceKind.Other ? null : kind, false));
        }

        return cards;
    }

    private static Surface? SurfaceOf(RaceKind kind) => kind switch
    {
        RaceKind.Road => Surface.Road,
        RaceKind.Trail or RaceKind.Mountain or RaceKind.CrossCountry => Surface.Trail,
        _ => null,
    };

    [GeneratedRegex(@"^[A-Za-z0-9._~%/-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex ExternalIdPattern();

    [GeneratedRegex(@"[?&]page=(\d{1,4})(?![\d])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex PageParameter();
}
