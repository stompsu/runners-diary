using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Tests.Races;

/// <summary>Applies the real migrations (including <c>RaceCatalogue</c>) to a fresh SQL Server database and checks keys, cascades, SET NULL, checks and scoping.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class RaceCatalogueSchemaTests(ContainersApiFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> NewDatabaseAsync()
    {
        factory.SkipIfDockerUnavailable();
        var cs = new SqlConnectionStringBuilder(factory.SqlConnectionString) { InitialCatalog = "races_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        await using var db = CreateContext(cs, AnonymousCurrentUser.Instance);
        await db.Database.MigrateAsync(Ct);
        return cs;
    }

    private static RunnersDiaryDbContext CreateContext(string cs, ICurrentUser user) =>
        new(
            new DbContextOptionsBuilder<RunnersDiaryDbContext>()
                .UseSqlServer(cs, sql => sql.UseNetTopologySuite())
                .AddInterceptors(OwnershipGuardInterceptor.Instance)
                .Options,
            user);

    private static async Task<T> ScalarAsync<T>(string cs, string sql)
    {
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static RaceFacts Facts(string externalId, DateOnly? date = null, double[]? distances = null) => new(
        RaceSource.Demo, externalId, "Demo Test Race " + externalId, date ?? new DateOnly(2026, 12, 6), new TimeOnly(10, 0),
        "Gliwice", new GeoPoint(50.2945, 18.6714), "PL", "PL-24", RaceKind.Road, Surface.Road, distances ?? [10_000, 21_097.5, 5_000],
        25, "https://example.org/demo-signup", null, null, 60m, "PLN", null, "Demo Running Club");

    private static RunValues PlannedRun() => new()
    {
        Date = new DateOnly(2026, 12, 6),
        TimeZoneId = "Europe/Warsaw",
        Status = RunStatus.Planned,
        RunTypeId = 1,
        DistanceMeters = 10_000,
        Race = new RaceDetails("Demo Test Race", RaceDistanceCategory.TenK),
    };

    private static async Task AddUserAsync(string cs, User user)
    {
        await using var db = CreateContext(cs, new FixedCurrentUser(user.Id, isGuest: true));
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<Race> AddRaceAsync(string cs, RaceFacts facts)
    {
        var race = Race.Create(facts, Now);
        await using var db = CreateContext(cs, AnonymousCurrentUser.Instance);
        db.Races.Add(race);
        await db.SaveChangesAsync(Ct);
        return race;
    }

    [Fact]
    public async Task The_migration_creates_the_catalogue_with_the_documented_keys_indexes_and_cascades()
    {
        var cs = await NewDatabaseAsync();

        Assert.Equal("NONCLUSTERED", await ScalarAsync<string>(cs, "SELECT type_desc FROM sys.indexes WHERE object_id = OBJECT_ID('Races') AND is_primary_key = 1"));
        Assert.Equal("Date", await ScalarAsync<string>(cs, """
            SELECT c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.name = 'CX_Races_Date' AND i.type_desc = 'CLUSTERED' AND i.object_id = OBJECT_ID('Races')
            """));
        Assert.Equal(1, await ScalarAsync<int>(cs, "SELECT CAST(is_unique AS int) FROM sys.indexes WHERE name = 'UX_Races_Source_External'"));
        Assert.Equal("([DuplicateOfRaceId] IS NOT NULL)", await ScalarAsync<string>(cs, "SELECT filter_definition FROM sys.indexes WHERE name = 'IX_Races_DuplicateOf'"));
        Assert.Equal(1, await ScalarAsync<int>(cs, "SELECT CAST(is_unique AS int) FROM sys.indexes WHERE name = 'UX_Runs_User_CatalogRace' AND has_filter = 1"));
        Assert.Equal("CLUSTERED", await ScalarAsync<string>(cs, "SELECT type_desc FROM sys.indexes WHERE object_id = OBJECT_ID('RaceProposalStates') AND is_primary_key = 1"));
        Assert.Equal("CLUSTERED", await ScalarAsync<string>(cs, "SELECT type_desc FROM sys.indexes WHERE object_id = OBJECT_ID('RaceDistances') AND is_primary_key = 1"));

        Assert.Equal("SET_NULL", await ScalarAsync<string>(cs, "SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE name = 'FK_Runs_Races_CatalogRaceId'"));
        Assert.Equal("NO_ACTION", await ScalarAsync<string>(cs, "SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE name = 'FK_Races_Races_DuplicateOfRaceId'"));
        Assert.Equal(4, await ScalarAsync<int>(cs, """
            SELECT COUNT(*) FROM sys.foreign_keys WHERE delete_referential_action_desc = 'CASCADE'
              AND name IN ('FK_RaceDistances_Races_RaceId', 'FK_RaceProposalStates_Races_RaceId', 'FK_RaceProposalStates_Users_UserId', 'FK_RaceInterestProfiles_Users_UserId')
            """));

        Assert.Equal("varchar", await ScalarAsync<string>(cs, "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='RaceInterestProfiles' AND COLUMN_NAME='RegionCodes'"));
        Assert.Equal(200, await ScalarAsync<int>(cs, "SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='RaceInterestProfiles' AND COLUMN_NAME='RegionCodes'"));
        Assert.Equal("binary", await ScalarAsync<string>(cs, "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Races' AND COLUMN_NAME='FactsHash'"));
        Assert.Equal("geography", await ScalarAsync<string>(cs, "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Races' AND COLUMN_NAME='Point'"));

        await using var db = CreateContext(cs, AnonymousCurrentUser.Instance);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Deleting_a_race_keeps_the_linked_run_with_a_null_link_and_removes_its_distances_and_states()
    {
        var cs = await NewDatabaseAsync();
        var user = User.CreateGuest(Now);
        await AddUserAsync(cs, user);
        var race = await AddRaceAsync(cs, Facts("delete-me"));
        var other = await AddRaceAsync(cs, Facts("keep-me"));
        Guid runId;

        await using (var db = CreateContext(cs, new FixedCurrentUser(user.Id, isGuest: true)))
        {
            var run = Run.Create(user.Id, PlannedRun(), Now);
            run.LinkToCatalogRace(race.Id);
            runId = run.Id;
            db.Runs.Add(run);
            db.RaceProposalStates.AddRange(
                RaceProposalState.Create(user.Id, race.Id, RaceProposalMark.Interested, race.Date, Now),
                RaceProposalState.Create(user.Id, other.Id, RaceProposalMark.Dismissed, other.Date, Now));
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(3, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM RaceDistances WHERE RaceId = '{race.Id}'"));

        // What the retention purge does (anonymous context, no Owner filter on Races): one DELETE; the database cascades and sets NULL.
        await using (var job = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            Assert.Equal(1, await job.Races.Where(r => r.Id == race.Id).ExecuteDeleteAsync(Ct));
        }

        await using var asUser = CreateContext(cs, new FixedCurrentUser(user.Id, isGuest: true));
        var survivor = await asUser.Runs.AsNoTracking().SingleAsync(r => r.Id == runId, Ct);
        Assert.Null(survivor.CatalogRaceId);
        Assert.Equal("Demo Test Race", survivor.Race!.Name);
        Assert.Equal(0, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM RaceDistances WHERE RaceId = '{race.Id}'"));
        var states = await asUser.RaceProposalStates.AsNoTracking().ToListAsync(Ct);
        Assert.Equal(other.Id, Assert.Single(states).RaceId);
        Assert.Equal(3, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM RaceDistances WHERE RaceId = '{other.Id}'"));
    }

    [Fact]
    public async Task Deleting_a_race_also_works_through_the_change_tracker()
    {
        var cs = await NewDatabaseAsync();
        var user = User.CreateGuest(Now);
        await AddUserAsync(cs, user);
        var race = await AddRaceAsync(cs, Facts("tracked-delete"));
        await using (var db = CreateContext(cs, new FixedCurrentUser(user.Id, isGuest: true)))
        {
            var run = Run.Create(user.Id, PlannedRun(), Now);
            run.LinkToCatalogRace(race.Id);
            db.Runs.Add(run);
            await db.SaveChangesAsync(Ct);
        }

        await using (var job = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            var tracked = await job.Races.Include(r => r.Distances).SingleAsync(r => r.Id == race.Id, Ct);
            job.Races.Remove(tracked);
            await job.SaveChangesAsync(Ct);
        }

        Assert.Equal(1, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM Runs WHERE CatalogRaceId IS NULL"));
        Assert.Equal(0, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM RaceDistances"));
    }

    [Fact]
    public async Task A_race_round_trips_and_a_changed_distance_set_replaces_only_the_differing_rows()
    {
        var cs = await NewDatabaseAsync();
        var race = await AddRaceAsync(cs, Facts("round-trip"));

        await using (var db = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            var stored = await db.Races.Include(r => r.Distances).SingleAsync(r => r.Id == race.Id, Ct);
            Assert.Equal([5_000, 10_000, 21_097.5], stored.DistancesMeters);
            Assert.Equal(new GeoPoint(50.2945, 18.6714), stored.Point);
            Assert.Equal(race.FactsHash, stored.FactsHash);
            Assert.Equal(32, stored.FactsHash.Length);
            Assert.Equal("Europe/Warsaw", stored.TimeZoneId);

            stored.ApplyFacts(Facts("round-trip", new DateOnly(2026, 12, 13), [10_000, 42_195]), Now.AddDays(1));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            var stored = await db.Races.Include(r => r.Distances).SingleAsync(r => r.Id == race.Id, Ct);
            Assert.Equal([10_000, 42_195], stored.DistancesMeters);
            Assert.Equal(new DateOnly(2026, 12, 6), stored.PreviousDate);
            Assert.Equal(new DateOnly(2026, 12, 13), stored.Date);
        }

        Assert.Equal(4326, await ScalarAsync<int>(cs, "SELECT Point.STSrid FROM Races"));
    }

    [Fact]
    public async Task The_natural_key_the_price_check_and_the_duplicate_check_are_enforced_by_the_database()
    {
        var cs = await NewDatabaseAsync();
        var race = await AddRaceAsync(cs, Facts("unique"));

        await using (var db = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            db.Races.Add(Race.Create(Facts("unique"), Now));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        await AssertSqlFailsAsync(cs, "UPDATE Races SET PriceAmount = NULL WHERE Id = '" + race.Id + "'", "CK_Races_Price");
        await AssertSqlFailsAsync(cs, "UPDATE Races SET DuplicateOfRaceId = Id WHERE Id = '" + race.Id + "'", "CK_Races_Duplicate");
    }

    [Fact]
    public async Task A_user_can_link_at_most_one_run_per_race_but_two_users_can_link_the_same_race()
    {
        var cs = await NewDatabaseAsync();
        var a = User.CreateGuest(Now);
        var b = User.CreateGuest(Now);
        await AddUserAsync(cs, a);
        await AddUserAsync(cs, b);
        var race = await AddRaceAsync(cs, Facts("linked"));

        foreach (var user in new[] { a, b })
        {
            await using var db = CreateContext(cs, new FixedCurrentUser(user.Id, isGuest: true));
            var run = Run.Create(user.Id, PlannedRun(), Now);
            run.LinkToCatalogRace(race.Id);
            db.Runs.Add(run);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = CreateContext(cs, new FixedCurrentUser(a.Id, isGuest: true)))
        {
            var second = Run.Create(a.Id, PlannedRun(), Now);
            second.LinkToCatalogRace(race.Id);
            db.Runs.Add(second);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        // Unlinked runs are not constrained (filtered index).
        await using (var db = CreateContext(cs, new FixedCurrentUser(a.Id, isGuest: true)))
        {
            db.Runs.AddRange(Run.Create(a.Id, PlannedRun(), Now), Run.Create(a.Id, PlannedRun(), Now));
            await db.SaveChangesAsync(Ct);
        }
    }

    [Fact]
    public async Task Interest_profiles_and_states_are_owner_filtered_and_the_profile_round_trips()
    {
        var cs = await NewDatabaseAsync();
        var a = User.CreateGuest(Now);
        var b = User.CreateGuest(Now);
        await AddUserAsync(cs, a);
        await AddUserAsync(cs, b);
        var race = await AddRaceAsync(cs, Facts("scoped"));
        var values = new RaceInterestValues
        {
            Kinds = [RaceKind.Road, RaceKind.Mountain],
            RegionCodes = ["PL-24", "PL-12"],
            Center = new GeoPoint(50.2945, 18.6714),
            CenterName = "Gliwice",
            RadiusMeters = 60_000,
            MinDistanceMeters = 5_000,
            MaxDistanceMeters = 42_195,
            HorizonDays = 120,
            Keywords = "night",
            ShowInCalendar = true,
        };

        await using (var db = CreateContext(cs, new FixedCurrentUser(a.Id, isGuest: true)))
        {
            db.RaceInterestProfiles.Add(RaceInterestProfile.Create(a.Id, values, Now));
            db.RaceProposalStates.Add(RaceProposalState.Create(a.Id, race.Id, RaceProposalMark.Interested, race.Date, Now));
            await db.SaveChangesAsync(Ct);
        }

        await using (var asB = CreateContext(cs, new FixedCurrentUser(b.Id, isGuest: true)))
        {
            Assert.Empty(await asB.RaceInterestProfiles.ToListAsync(Ct));
            Assert.Empty(await asB.RaceProposalStates.ToListAsync(Ct));
        }

        await using (var anonymous = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            Assert.Empty(await anonymous.RaceInterestProfiles.ToListAsync(Ct));
            Assert.Single(await anonymous.RaceInterestProfiles.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).ToListAsync(Ct));
            // The catalogue itself has no Owner filter: every caller reads it.
            Assert.Single(await anonymous.Races.ToListAsync(Ct));
        }

        await using (var asB = CreateContext(cs, new FixedCurrentUser(b.Id, isGuest: true)))
        {
            Assert.Single(await asB.Races.ToListAsync(Ct));
            asB.RaceInterestProfiles.Add(RaceInterestProfile.Create(a.Id, values, Now));
            await Assert.ThrowsAsync<InvalidOperationException>(() => asB.SaveChangesAsync(Ct)); // ownership guard
        }

        await using (var asA = CreateContext(cs, new FixedCurrentUser(a.Id, isGuest: true)))
        {
            var profile = await asA.RaceInterestProfiles.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(RaceKinds.Road | RaceKinds.Mountain, profile.Kinds);
            Assert.Equal(["PL-12", "PL-24"], profile.RegionCodes);
            Assert.Equal(new GeoPoint(50.2945, 18.6714), profile.Center);
            Assert.Equal("Gliwice", profile.CenterName);
            Assert.Equal(60_000, profile.RadiusMeters);
            Assert.Equal(120, profile.HorizonDays);
            Assert.True(profile.ShowInCalendar);
        }

        Assert.Equal("""["PL-12","PL-24"]""", await ScalarAsync<string>(cs, "SELECT RegionCodes FROM RaceInterestProfiles"));
    }

    [Fact]
    public async Task Profile_check_constraints_reject_a_radius_without_a_center_and_an_inverted_distance_range()
    {
        var cs = await NewDatabaseAsync();
        var user = User.CreateGuest(Now);
        await AddUserAsync(cs, user);
        await using (var db = CreateContext(cs, new FixedCurrentUser(user.Id, isGuest: true)))
        {
            db.RaceInterestProfiles.Add(RaceInterestProfile.Create(user.Id, RaceInterestValues.Defaults, Now));
            await db.SaveChangesAsync(Ct);
        }

        await AssertSqlFailsAsync(cs, "UPDATE RaceInterestProfiles SET RadiusMeters = 10000", "CK_RaceInterestProfiles_Center");
        await AssertSqlFailsAsync(cs, "UPDATE RaceInterestProfiles SET MinDistanceMeters = 20000, MaxDistanceMeters = 10000", "CK_RaceInterestProfiles_Distance");
    }

    private static async Task AssertSqlFailsAsync(string cs, string sql, string constraint)
    {
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(sql, connection);
        var ex = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(Ct));
        Assert.Contains(constraint, ex.Message, StringComparison.Ordinal);
    }
}
