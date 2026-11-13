using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.RaceSources;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Api.Tests.RaceSources;

/// <summary>A source whose scan result a test scripts.</summary>
internal sealed class ScriptedSource(RaceSource source, Func<RaceScanContext, RaceScanResult> scan) : IRaceSource
{
    public RaceSource Source => source;

    public string Id => RaceSourceRegistry.IdOf(source);

    public int Scans { get; private set; }

    public Task<RaceScanResult> ScanAsync(RaceScanContext context, CancellationToken cancellationToken)
    {
        Scans++;
        return Task.FromResult(scan(context));
    }
}

/// <summary>A fresh, migrated database in the shared SQL container plus the services the import service needs.</summary>
internal sealed class ImportHarness : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 11, 13, 3, 25, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;

    private ImportHarness(string connectionString, ServiceProvider services, FakeTimeProvider clock, SqlActivityInterceptor interceptor)
    {
        ConnectionString = connectionString;
        _services = services;
        Clock = clock;
        Interceptor = interceptor;
    }

    public string ConnectionString { get; }

    public FakeTimeProvider Clock { get; }

    public SqlActivityInterceptor Interceptor { get; }

    public IServiceScopeFactory Scopes => _services.GetRequiredService<IServiceScopeFactory>();

    public DemoRaceCatalogue DemoCatalogue => _services.GetRequiredService<DemoRaceCatalogue>();

    public static async Task<string> NewDatabaseAsync(ContainersApiFactory factory)
    {
        factory.SkipIfDockerUnavailable();
        var cs = new SqlConnectionStringBuilder(factory.SqlConnectionString) { InitialCatalog = "race_import_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        await using var db = Context(cs, AnonymousCurrentUser.Instance);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return cs;
    }

    public static async Task<ImportHarness> CreateAsync(ContainersApiFactory factory)
    {
        var cs = await NewDatabaseAsync(factory);
        var clock = new FakeTimeProvider(Start);
        var interceptor = new SqlActivityInterceptor();
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(clock)
            .AddScoped<ICurrentUser>(_ => AnonymousCurrentUser.Instance)
            .AddDbContext<RunnersDiaryDbContext>(o => o.UseSqlServer(cs, sql => sql.UseNetTopologySuite()).AddInterceptors(OwnershipGuardInterceptor.Instance, interceptor))
            .AddSingleton(Options.Create(new RacesOptions()))
            .AddLogging()
            .AddSingleton<DemoRaceCatalogue>()
            .BuildServiceProvider();
        return new ImportHarness(cs, services, clock, interceptor);
    }

    public static RunnersDiaryDbContext Context(string cs, ICurrentUser user) =>
        new(
            new DbContextOptionsBuilder<RunnersDiaryDbContext>()
                .UseSqlServer(cs, sql => sql.UseNetTopologySuite())
                .AddInterceptors(OwnershipGuardInterceptor.Instance)
                .Options,
            user);

    public RunnersDiaryDbContext Db() => Context(ConnectionString, AnonymousCurrentUser.Instance);

    public RaceImportService Service(RaceImportOptions options, params IRaceSource[] sources) =>
        new(Scopes, sources, Microsoft.Extensions.Options.Options.Create(options), Clock, NullLogger<RaceImportService>.Instance);

    public static RaceImportOptions DemoOptions(Action<RaceImportOptions>? change = null)
    {
        var options = new RaceImportOptions { Enabled = true, Sources = { Demo = { Enabled = true } } };
        change?.Invoke(options);
        return options;
    }

    public async Task<List<Race>> RacesAsync()
    {
        await using var db = Db();
        return await db.Races.AsNoTracking().Include(r => r.Distances).OrderBy(r => r.ExternalId).ToListAsync(TestContext.Current.CancellationToken);
    }

    public async Task AddRacesAsync(params Race[] races)
    {
        await using var db = Db();
        db.Races.AddRange(races);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    /// <summary>Demo-source facts with a name that is not similar to any other test race.</summary>
    public static RaceFacts Facts(string id, DateOnly date, string? name = null, GeoPoint? point = null, string? locality = "Gliwice") => new(
        RaceSource.Demo, id, name ?? $"Zebra {id}", date, null, locality, point ?? new GeoPoint(50.2945, 18.6714), "PL", "PL-24", RaceKind.Road, Surface.Road,
        [10_000], null, null, null, null, null, null, null, null);

    public static RaceScanResult Scan(IEnumerable<RaceFacts> facts, bool complete = true, RaceScanOutcome outcome = RaceScanOutcome.Completed, params DetailFetchRecord[] fetches) =>
        new(facts.ToList(), complete, outcome, RaceFetchStats.Empty, fetches);
}
