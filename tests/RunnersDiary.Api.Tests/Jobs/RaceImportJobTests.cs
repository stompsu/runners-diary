using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Api.Http;
using RunnersDiary.Api.Jobs;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Api.Tests.RaceSources;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.RaceSources;
using RunnersDiary.Infrastructure.RaceSources.RunningLife;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Jobs;

/// <summary>Host composition, configuration rules and exit codes of <c>--job race-import</c> that need no database (ADR 0038 §1).</summary>
public sealed class RaceImportJobHostTests
{
    private const string UnreachableSql = "Server=127.0.0.1,1;Database=runnersdiary;User Id=test;Password=unused;Connect Timeout=2;TrustServerCertificate=true";

    private static string[] Args(string connectionString, params string[] extra) =>
    [
        "--job", "race-import",
        "--environment=Testing",
        $"--ConnectionStrings:runnersdiary={connectionString}",
        "--Aspire:Microsoft:EntityFrameworkCore:SqlServer:DisableRetry=true",
        .. extra,
    ];

    [Fact]
    public async Task A_disabled_import_exits_with_0_and_opens_no_sql_connection()
    {
        var interceptor = new SqlActivityInterceptor();
        bool? hasServer = null;
        bool? hasBlobs = null;
        bool? hasGuestCleanup = null;
        bool? hasHostedImport = null;

        // Unreachable SQL: a connection attempt would fail the run, so exit code 0 proves that none was made; the interceptor counts opened connections.
        var exitCode = await JobMode.RunAsync("race-import", Args(UnreachableSql, "--RaceImport:Enabled=false", "--RaceImport:Sources:RunningLife:Enabled=true"), builder =>
        {
            builder.Services.AddSingleton(interceptor);
            hasServer = builder.Services.Any(d => d.ServiceType == typeof(IServer));
            hasBlobs = builder.Services.Any(d => d.ServiceType == typeof(IBlobStore));
            hasGuestCleanup = builder.Services.Any(d => d.ServiceType == typeof(GuestCleanup) || d.ImplementationType == typeof(GuestCleanupService));
            hasHostedImport = builder.Services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Namespace?.Contains("RaceSources", StringComparison.Ordinal) == true);
        });

        Assert.Equal(0, exitCode);
        Assert.Equal(0, interceptor.OpenedCount);
        Assert.False(hasServer, "Job mode must not register Kestrel.");
        Assert.False(hasBlobs, "The race-import profile has no Blob access.");
        Assert.False(hasGuestCleanup, "The race-import profile has no guest services.");
        Assert.False(hasHostedImport, "The import runs explicitly, not as a hosted service.");
    }

    [Fact]
    public async Task Enabled_globally_but_with_every_source_off_is_also_a_no_op_without_sql()
    {
        var interceptor = new SqlActivityInterceptor();

        var exitCode = await JobMode.RunAsync("race-import", Args(UnreachableSql, "--RaceImport:Enabled=true"), builder => builder.Services.AddSingleton(interceptor));

        Assert.Equal(0, exitCode);
        Assert.Equal(0, interceptor.OpenedCount);
    }

    [Fact]
    public async Task The_default_configuration_has_everything_off()
    {
        var interceptor = new SqlActivityInterceptor();

        // No RaceImport arguments at all: appsettings.json (all off) applies.
        var exitCode = await JobMode.RunAsync("race-import", Args(UnreachableSql), builder => builder.Services.AddSingleton(interceptor));

        Assert.Equal(0, exitCode);
        Assert.Equal(0, interceptor.OpenedCount);
    }

    [Fact]
    public async Task A_minimum_interval_below_one_second_fails_the_job_at_startup()
    {
        var exitCode = await JobMode.RunAsync("race-import", Args(UnreachableSql, "--RaceImport:Enabled=false", "--RaceImport:MinInterval=00:00:00.500"));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task An_unreachable_database_with_the_import_enabled_exits_with_1()
    {
        var exitCode = await JobMode.RunAsync("race-import", Args(UnreachableSql, "--RaceImport:Enabled=true", "--RaceImport:Sources:Demo:Enabled=true"));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task The_production_job_needs_the_database_connection_string_but_not_the_blob_one()
    {
        Assert.Equal(1, await JobMode.RunAsync("race-import", ["--job", "race-import", "--environment=Production"]));
        // Only the database string: the race-import profile starts (disabled, so it exits 0 without connecting); guest-cleanup would demand blobs too.
        Assert.Equal(0, await JobMode.RunAsync("race-import", ["--job", "race-import", "--environment=Production", $"--ConnectionStrings:runnersdiary={UnreachableSql}"]));
        Assert.Equal(1, await JobMode.RunAsync("guest-cleanup", ["--job", "guest-cleanup", "--environment=Production", $"--ConnectionStrings:runnersdiary={UnreachableSql}"]));
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "RunnersDiary.Api";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        var data = new Dictionary<string, string?> { ["ConnectionStrings:runnersdiary"] = "Server=tcp:x,1433" };
        foreach (var (key, value) in values)
        {
            data[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    [Fact]
    public void The_demo_source_is_rejected_in_production_but_fine_elsewhere()
    {
        var config = Config(("RaceImport:Sources:Demo:Enabled", "true"));

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.ValidateJob(config, new Env("Production"), JobMode.RaceImportJob));
        Assert.Contains("RaceImport:Sources:Demo:Enabled", ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.ValidateApi(config, new Env("Production")));
        ProductionConfigurationValidator.ValidateJob(config, new Env("Development"), JobMode.RaceImportJob);
        ProductionConfigurationValidator.ValidateJob(config, new Env("Testing"), JobMode.RaceImportJob);
    }

    [Theory]
    [InlineData("00:00:00.999")]
    [InlineData("00:00:00")]
    [InlineData("not-a-time")]
    public void A_minimum_interval_below_one_second_or_malformed_is_rejected_in_production(string interval)
    {
        var config = Config(("RaceImport:MinInterval", interval));

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.ValidateJob(config, new Env("Production"), JobMode.RaceImportJob));
        Assert.Contains("RaceImport:MinInterval", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("00:00:01.500")]
    [InlineData("00:00:05")]
    public void The_default_and_slower_intervals_pass_in_production(string interval) =>
        ProductionConfigurationValidator.ValidateJob(Config(("RaceImport:MinInterval", interval), ("RaceImport:Enabled", "true"), ("RaceImport:Sources:RunningLife:Enabled", "true")), new Env("Production"), JobMode.RaceImportJob);

    [Fact]
    public void An_unknown_job_name_is_still_rejected_and_both_known_names_are_listed() =>
        Assert.Equal(["guest-cleanup", "race-import"], new[] { JobMode.GuestCleanupJob, JobMode.RaceImportJob }.Order(StringComparer.Ordinal));
}

/// <summary>The job against a real database: Demo import idempotence, the lock, a layout change, and the least-privilege SQL principal of ADR 0038 §2.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class RaceImportJobDatabaseTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Args(string connectionString, params string[] extra) =>
    [
        "--job", "race-import",
        "--environment=Testing",
        $"--ConnectionStrings:runnersdiary={connectionString}",
        "--Aspire:Microsoft:EntityFrameworkCore:SqlServer:DisableRetry=true",
        "--RaceImport:Enabled=true",
        .. extra,
    ];

    private static readonly string[] DemoOn = ["--RaceImport:Sources:Demo:Enabled=true"];

    private static async Task<List<Race>> RacesAsync(string cs)
    {
        await using var db = ImportHarness.Context(cs, AnonymousCurrentUser.Instance);
        return await db.Races.AsNoTracking().ToListAsync(Ct);
    }

    [Fact]
    public async Task The_demo_source_imports_once_and_a_second_run_changes_nothing()
    {
        var cs = await ImportHarness.NewDatabaseAsync(factory);

        Assert.Equal(0, await JobMode.RunAsync("race-import", Args(cs, DemoOn)));
        var first = await RacesAsync(cs);
        Assert.Equal(0, await JobMode.RunAsync("race-import", Args(cs, DemoOn)));
        var second = await RacesAsync(cs);

        Assert.Equal(40, first.Count);
        Assert.Equal(40, second.Count);
        Assert.All(second, r => Assert.Equal(r.ImportedUtc, r.UpdatedUtc)); // no facts were rewritten
        Assert.All(second, r => Assert.Equal(RaceSource.Demo, r.Source));
    }

    [Fact]
    public async Task While_another_session_holds_the_lock_the_job_exits_with_0_and_imports_nothing()
    {
        var cs = await ImportHarness.NewDatabaseAsync(factory);
        await using (var other = new SqlConnection(cs))
        {
            await other.OpenAsync(Ct);
            await using var take = new SqlCommand(
                $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'{RaceImportService.LockResource}', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0; SELECT @r;", other);
            Assert.True((int)(await take.ExecuteScalarAsync(Ct))! >= 0);

            Assert.Equal(0, await JobMode.RunAsync("race-import", Args(cs, DemoOn)));
            Assert.Empty(await RacesAsync(cs));

            // A pooled connection keeps its session lock until it is reused; release it like a finished job does.
            await using var release = new SqlCommand($"EXEC sp_releaseapplock @Resource = N'{RaceImportService.LockResource}', @LockOwner = N'Session';", other);
            await release.ExecuteNonQueryAsync(Ct);
        }

        Assert.Equal(0, await JobMode.RunAsync("race-import", Args(cs, DemoOn)));
        Assert.Equal(40, (await RacesAsync(cs)).Count);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private static void UseRunningLifeStub(HostApplicationBuilder builder, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        builder.Services.RemoveAll<IRaceSource>();
        builder.Services.AddSingleton<IRaceSource>(sp => new RunningLifeSource(
            new HttpClient(new StubHandler(respond)), sp.GetRequiredService<IOptions<RaceImportOptions>>(), TimeProvider.System, sp.GetRequiredService<ILogger<RunningLifeSource>>()));
    }

    [Fact]
    public async Task A_layout_change_fails_the_job_with_1_and_leaves_the_catalogue_alone()
    {
        var cs = await ImportHarness.NewDatabaseAsync(factory);
        var existing = Race.Create(ImportHarness.Facts("keep", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)) with
        {
            Source = RaceSource.RunningLife,
            SourceUrl = "https://running.life/pl/wydarzenie/keep",
        }, DateTimeOffset.UtcNow);
        await using (var db = ImportHarness.Context(cs, AnonymousCurrentUser.Instance))
        {
            db.Races.Add(existing);
            await db.SaveChangesAsync(Ct);
        }

        var exitCode = await JobMode.RunAsync(
            "race-import",
            Args(cs, "--RaceImport:Sources:RunningLife:Enabled=true", "--RaceImport:MinInterval=00:00:01"),
            builder => UseRunningLifeStub(builder, r => r.RequestUri!.AbsolutePath == "/robots.txt"
                ? RaceSourceTestSupport.Text(RaceSourceTestSupport.Fixture("robots-allow.txt"))
                : RaceSourceTestSupport.Html(RaceSourceTestSupport.Fixture("listing-empty-layout.html"))));

        Assert.Equal(1, exitCode);
        var left = Assert.Single(await RacesAsync(cs));
        Assert.Null(left.RemovedUtc);
        Assert.Equal(0, left.MissedScans);
    }

    [Fact]
    public async Task A_healthy_running_life_listing_imports_through_the_job_and_exits_with_0()
    {
        var cs = await ImportHarness.NewDatabaseAsync(factory);

        var exitCode = await JobMode.RunAsync(
            "race-import",
            Args(cs, "--RaceImport:Sources:RunningLife:Enabled=true", "--RaceImport:MinInterval=00:00:01", "--RaceImport:MaxListingPages=1"),
            builder => UseRunningLifeStub(builder, r => r.RequestUri!.AbsolutePath switch
            {
                "/robots.txt" => RaceSourceTestSupport.Text(RaceSourceTestSupport.Fixture("robots-allow.txt")),
                "/kalendarz-biegow/polska" => RaceSourceTestSupport.Html(RaceSourceTestSupport.Fixture("listing-p1.html")),
                "/pl/wydarzenie/bieg-testowy-w-gliwicach" => RaceSourceTestSupport.Html(RaceSourceTestSupport.Fixture("detail-multi-distance.html")),
                _ => RaceSourceTestSupport.Html(RaceSourceTestSupport.Fixture("detail-no-distances.html")),
            }));

        Assert.Equal(0, exitCode);
        await using var db = ImportHarness.Context(cs, AnonymousCurrentUser.Instance);
        var races = await db.Races.AsNoTracking().Include(r => r.Distances).OrderBy(r => r.ExternalId).ToListAsync(Ct);
        Assert.Equal(["bieg-testowy-w-gliwicach", "trail-testowy-beskidy", "wymyslony-polmaraton-nocny/2027-04-03"], races.Select(r => r.ExternalId));
        var gliwice = races[0];
        Assert.Equal(RaceSource.RunningLife, gliwice.Source);
        Assert.Equal([5_000, 10_000, 21_097.5, 42_195], gliwice.DistancesMeters);
        Assert.Equal("PL-24", gliwice.RegionCode);
        Assert.Equal("https://zapisy.example/bieg-testowy?edycja=5", gliwice.SignupUrl);
        Assert.Equal("https://running.life/pl/wydarzenie/bieg-testowy-w-gliwicach", gliwice.SourceUrl);
        Assert.NotNull(gliwice.DetailFetchedUtc);
        Assert.Equal("Trail <b>Pogrubiony</b> & <script>alert(1)</script>", races[1].Name);
    }

    [Fact]
    public async Task A_principal_with_only_the_two_table_grants_can_import_and_purge_with_the_cascades()
    {
        var cs = await ImportHarness.NewDatabaseAsync(factory);
        var database = new SqlConnectionStringBuilder(cs).InitialCatalog;
        var login = "rd_race_import_" + Guid.NewGuid().ToString("N")[..8];
        var password = "Rd!" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Admin setup: a user with a run and a proposal state on a race that the retention purge will delete.
        var user = User.CreateGuest(now);
        var ancient = Race.Create(ImportHarness.Facts("ancient", today.AddDays(-400)), now);
        Guid runId;
        await using (var db = ImportHarness.Context(cs, new FixedCurrentUser(user.Id, isGuest: true)))
        {
            db.Users.Add(user);
            db.Races.Add(ancient);
            await db.SaveChangesAsync(Ct);
            var run = Run.Create(user.Id, new RunValues
            {
                Date = today.AddDays(-400),
                TimeZoneId = "Europe/Warsaw",
                Status = RunStatus.Planned,
                RunTypeId = 1,
                DistanceMeters = 10_000,
                Race = new RaceDetails("Ancient", RaceDistanceCategory.TenK),
            }, now);
            run.LinkToCatalogRace(ancient.Id);
            runId = run.Id;
            db.Runs.Add(run);
            db.RaceProposalStates.Add(RaceProposalState.Create(user.Id, ancient.Id, RaceProposalMark.Interested, ancient.Date, now));
            await db.SaveChangesAsync(Ct);
        }

        await ExecuteAsync(factory.SqlConnectionString, $"CREATE LOGIN [{login}] WITH PASSWORD = '{password}', CHECK_POLICY = OFF;");
        try
        {
            await ExecuteAsync(cs, $"""
                CREATE USER [{login}] FOR LOGIN [{login}];
                GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Races TO [{login}];
                GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.RaceDistances TO [{login}];
                """);
            var restricted = new SqlConnectionStringBuilder(cs) { UserID = login, Password = password, IntegratedSecurity = false }.ConnectionString;

            Assert.Equal(0, await JobMode.RunAsync("race-import", Args(restricted, DemoOn)));

            var races = await RacesAsync(cs);
            Assert.Equal(40, races.Count);
            Assert.DoesNotContain(races, r => r.ExternalId == "ancient");
            await using var asUser = ImportHarness.Context(cs, new FixedCurrentUser(user.Id, isGuest: true));
            Assert.Null((await asUser.Runs.AsNoTracking().SingleAsync(r => r.Id == runId, Ct)).CatalogRaceId); // SET NULL needs no grant on Runs
            Assert.Empty(await asUser.RaceProposalStates.AsNoTracking().ToListAsync(Ct)); // nor the cascade on RaceProposalStates

            // The principal really is restricted: it cannot read the users' data.
            await using var probe = new SqlConnection(restricted);
            await probe.OpenAsync(Ct);
            await using var select = new SqlCommand("SELECT COUNT(*) FROM dbo.Runs", probe);
            await Assert.ThrowsAsync<SqlException>(() => select.ExecuteScalarAsync(Ct));
        }
        finally
        {
            SqlConnection.ClearAllPools(); // pooled connections of the login would block DROP LOGIN
            await ExecuteAsync(factory.SqlConnectionString, $"USE [{database}]; DROP USER IF EXISTS [{login}]; USE master; DROP LOGIN [{login}];");
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
