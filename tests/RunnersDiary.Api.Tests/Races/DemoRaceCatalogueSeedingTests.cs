using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Races;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Api.Tests.Races;

/// <summary>The demo catalogue seeder against a real database: idempotent, and a moved date is recorded.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class DemoRaceCatalogueSeedingTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Today = new(2026, 11, 13);

    private async Task<(DemoRaceCatalogue Catalogue, ServiceProvider Services)> NewCatalogueAsync()
    {
        factory.SkipIfDockerUnavailable();
        var cs = new SqlConnectionStringBuilder(factory.SqlConnectionString) { InitialCatalog = "demo_races_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        var services = new ServiceCollection()
            .AddScoped<ICurrentUser>(_ => AnonymousCurrentUser.Instance)
            .AddDbContext<RunnersDiaryDbContext>(o => o.UseSqlServer(cs, sql => sql.UseNetTopologySuite()).AddInterceptors(OwnershipGuardInterceptor.Instance))
            .BuildServiceProvider();
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>().Database.MigrateAsync(Ct);
        }

        var catalogue = new DemoRaceCatalogue(
            Options.Create(new RacesOptions()), services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<DemoRaceCatalogue>.Instance);
        return (catalogue, services);
    }

    [Fact]
    public async Task Seeding_inserts_the_whole_catalogue_once_and_a_second_run_changes_nothing()
    {
        var (catalogue, services) = await NewCatalogueAsync();
        await using var _ = services;
        var facts = catalogue.Validate(Today);

        Assert.Equal(40, await catalogue.SeedAsync(facts, Ct));
        Assert.Equal(0, await catalogue.SeedAsync(facts, Ct));

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();
        var races = await db.Races.AsNoTracking().Include(r => r.Distances).ToListAsync(Ct);
        Assert.Equal(40, races.Count);
        Assert.All(races, r => Assert.Equal(RaceSource.Demo, r.Source));
        Assert.Equal(40, races.Select(r => r.ExternalId).Distinct().Count());
        Assert.Contains(races, r => r.DistancesMeters.Count == 3);
        var unchanged = races.Single(r => r.ExternalId == "demo-01");
        Assert.Equal(unchanged.ImportedUtc, unchanged.UpdatedUtc);
    }

    [Fact]
    public async Task A_moved_date_updates_the_race_in_place_and_records_the_previous_date()
    {
        var (catalogue, services) = await NewCatalogueAsync();
        await using var _ = services;
        var facts = catalogue.Validate(Today);
        await catalogue.SeedAsync(facts, Ct);

        var moved = facts.Select(f => f.ExternalId == "demo-02" ? f with { Date = f.Date.AddDays(7), DistancesMeters = [21_097.5, 10_000] } : f).ToList();
        Assert.Equal(1, await catalogue.SeedAsync(moved, Ct));
        Assert.Equal(0, await catalogue.SeedAsync(moved, Ct));

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();
        var race = await db.Races.AsNoTracking().Include(r => r.Distances).SingleAsync(r => r.ExternalId == "demo-02", Ct);
        Assert.Equal(facts.Single(f => f.ExternalId == "demo-02").Date.AddDays(7), race.Date);
        Assert.Equal(facts.Single(f => f.ExternalId == "demo-02").Date, race.PreviousDate);
        Assert.NotNull(race.DateChangedUtc);
        Assert.Equal([10_000, 21_097.5], race.DistancesMeters);
        Assert.Equal(40, await db.Races.CountAsync(Ct));
    }
}
