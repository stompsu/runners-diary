using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Time;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Races;

/// <summary>
/// The synthetic demo race catalogue (source <see cref="RaceSource.Demo"/>, M8 plan §3.2): <c>seed/races/demo-races.json</c> with week-relative dates
/// resolved in the file's zone. <see cref="Expand"/> parses and normalises (strict: a typo fails startup), <see cref="Validate"/> is the startup check,
/// <see cref="SeedAsync(IReadOnlyList{RaceFacts}, CancellationToken)"/> upserts idempotently by <c>(Source, ExternalId)</c> and fact hash.
/// Writes go through an anonymous context: <c>Races</c> is shared reference data without an Owner filter and is not owned by anybody.
/// </summary>
public sealed partial class DemoRaceCatalogue(
    IOptions<RacesOptions> options,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<DemoRaceCatalogue> logger)
{
    public const string FileName = "demo-races.json";

    /// <summary>The guest demo profile needs this many future races in the Silesian voivodeship.</summary>
    public const int MinFutureSilesianRaces = 10;

    public const string SilesianRegion = "PL-24";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    private Template? _template;

    /// <summary>"Today" in the catalogue's zone.</summary>
    public DateOnly Today() => UserToday.Get(clock, Load().TimeZoneId);

    /// <summary>The normalised facts of every demo race for <paramref name="today"/>. Throws <see cref="DomainRuleException"/> on any invalid entry.</summary>
    public IReadOnlyList<RaceFacts> Expand(DateOnly today)
    {
        var template = Load();
        var monday = IsoWeek.MondayOf(today);
        var result = new List<RaceFacts>(template.Races.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in template.Races)
        {
            var date = monday.AddDays((r.Date.Week * 7) + (((int)ParseDay(r.Date.Day) + 6) % 7));
            var raw = new RaceFacts(
                RaceSource.Demo, r.Id, r.Name, date, r.StartTime, r.LocationName,
                r.Latitude is { } lat && r.Longitude is { } lon ? new GeoPoint(lat, lon) : null,
                r.CountryCode, r.RegionCode, r.Kind, r.Surface, r.DistancesMeters ?? [], r.ElevationGainMeters,
                r.SignupUrl, r.EventUrl, r.SourceUrl, r.PriceAmount, r.PriceCurrency, r.SignupDeadline, r.OrganizerName);

            if (!RaceFacts.TryNormalize(raw, out var facts, out var errors))
            {
                throw new DomainRuleException(errors.Select(e => new DomainError($"races[{r.Id}].{e.Path}", e.Message)).ToArray());
            }

            if (!ids.Add(facts.ExternalId))
            {
                throw new DomainRuleException($"Duplicate demo race id '{facts.ExternalId}'.");
            }

            result.Add(facts);
        }

        return result;
    }

    /// <summary>Startup check: the file parses, every race normalises, and enough future Silesian races exist for <paramref name="today"/>.</summary>
    public IReadOnlyList<RaceFacts> Validate(DateOnly today)
    {
        var facts = Expand(today);
        var future = facts.Count(f => f.Date > today && f.RegionCode == SilesianRegion);
        if (future < MinFutureSilesianRaces)
        {
            throw new InvalidOperationException($"The demo race catalogue has {future} future {SilesianRegion} races, fewer than the required {MinFutureSilesianRaces}.");
        }

        return facts;
    }

    public Task<int> SeedAsync(CancellationToken cancellationToken) => SeedAsync(Validate(Today()), cancellationToken);

    /// <summary>
    /// Inserts new races and applies changed facts (a different <c>FactsHash</c>); unchanged races are not touched, so a second run writes nothing.
    /// Returns the number of inserted plus updated races.
    /// </summary>
    public async Task<int> SeedAsync(IReadOnlyList<RaceFacts> facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var now = clock.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();

        var ids = facts.Select(f => f.ExternalId).ToList();
        var existing = await db.Races.Include(r => r.Distances)
            .Where(r => r.Source == RaceSource.Demo && ids.Contains(r.ExternalId))
            .ToDictionaryAsync(r => r.ExternalId, StringComparer.Ordinal, cancellationToken);

        var changes = 0;
        foreach (var fact in facts)
        {
            if (!RaceFacts.TryNormalize(fact, out var normalized, out var errors))
            {
                throw new DomainRuleException(errors);
            }

            if (!existing.TryGetValue(normalized.ExternalId, out var race))
            {
                db.Races.Add(Race.Create(normalized, now));
                changes++;
            }
            else if (!race.FactsHash.AsSpan().SequenceEqual(RaceFactsHasher.Hash(normalized)))
            {
                race.ApplyFacts(normalized, now);
                changes++;
            }
        }

        if (changes > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        LogSeeded(changes, facts.Count);
        return changes;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo race catalogue: {Changes} of {Total} races inserted or updated.")]
    private partial void LogSeeded(int changes, int total);

    private static DayOfWeek ParseDay(string? day) => day switch
    {
        "Mon" => DayOfWeek.Monday,
        "Tue" => DayOfWeek.Tuesday,
        "Wed" => DayOfWeek.Wednesday,
        "Thu" => DayOfWeek.Thursday,
        "Fri" => DayOfWeek.Friday,
        "Sat" => DayOfWeek.Saturday,
        "Sun" => DayOfWeek.Sunday,
        _ => throw new DomainRuleException($"Unknown day '{day}' (use Mon..Sun)."),
    };

    private Template Load()
    {
        var current = Volatile.Read(ref _template);
        if (current is not null)
        {
            return current;
        }

        var directory = options.Value.DemoCatalogue.Path;
        var root = Path.GetFullPath(Path.IsPathRooted(directory) ? directory : Path.Combine(AppContext.BaseDirectory, directory));
        var path = Path.Combine(root, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Demo race catalogue not found at '{path}'.", path);
        }

        Template parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Template>(File.ReadAllText(path), JsonOptions)
                ?? throw new DomainRuleException("The demo race catalogue is empty.");
        }
        catch (JsonException ex)
        {
            throw new DomainRuleException("The demo race catalogue is not valid: " + ex.Message, ex);
        }

        if (parsed.Version != 1 || string.IsNullOrWhiteSpace(parsed.TimeZoneId) || parsed.Races is null)
        {
            throw new DomainRuleException("The demo race catalogue needs version 1, timeZoneId and races.");
        }

        _ = IanaTimeZone.Get(parsed.TimeZoneId);
        return Interlocked.CompareExchange(ref _template, parsed, null) ?? parsed;
    }

    internal sealed record Template(int Version, string? Comment, string TimeZoneId, IReadOnlyList<Entry> Races);

    internal sealed record RelativeDate(int Week, string Day);

    internal sealed record Entry(
        string Id, string Name, RelativeDate Date, TimeOnly? StartTime, string? LocationName, double? Latitude, double? Longitude,
        string CountryCode, string? RegionCode, RaceKind Kind, Surface? Surface, IReadOnlyList<double>? DistancesMeters,
        double? ElevationGainMeters, decimal? PriceAmount, string? PriceCurrency, string? SignupUrl, string? EventUrl, string? SourceUrl,
        DateOnly? SignupDeadline, string? OrganizerName);
}

/// <summary>
/// Development/Testing only (<c>Races:DemoCatalogue:SeedOnStartup</c>): validates the demo catalogue (fails the host on a broken file) and upserts it
/// after the migrator has run (hosted services start in registration order).
/// </summary>
internal sealed class DemoRaceCatalogueSeeder(DemoRaceCatalogue catalogue) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken) => await catalogue.SeedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
