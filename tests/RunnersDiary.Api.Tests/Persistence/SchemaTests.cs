using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Tests.Persistence;

/// <summary>Applies the real migrations to a fresh database in the SQL container and verifies schema, seed data, mapping and scoping.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class SchemaTests(ContainersApiFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private async Task<string> NewDatabaseAsync()
    {
        factory.SkipIfDockerUnavailable();
        var builder = new SqlConnectionStringBuilder(factory.SqlConnectionString) { InitialCatalog = "schema_" + Guid.NewGuid().ToString("N") };
        var connectionString = builder.ConnectionString;
        await using var db = CreateContext(connectionString, AnonymousCurrentUser.Instance);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return connectionString;
    }

    private static RunnersDiaryDbContext CreateContext(string connectionString, ICurrentUser user) =>
        new(
            new DbContextOptionsBuilder<RunnersDiaryDbContext>()
                .UseSqlServer(connectionString, sql => sql.UseNetTopologySuite())
                .AddInterceptors(OwnershipGuardInterceptor.Instance)
                .Options,
            user);

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static RunValues SampleRun(Func<RunValues, RunValues>? change = null)
    {
        var v = new RunValues
        {
            Date = new DateOnly(2026, 10, 25),
            StartTime = new TimeOnly(9, 0),
            TimeZoneId = "Europe/Warsaw",
            Status = RunStatus.Completed,
            RunTypeId = 1,
            DistanceMeters = 10000,
            DurationSeconds = 2790,
            AvgHeartRate = 165,
            MaxHeartRate = 182,
            Rpe = 8,
            Location = new RunLocation(new GeoPoint(50.2945, 18.6714), "Park Chrobrego, Gliwice"),
            Race = new RaceDetails("Gliwice 10K", RaceDistanceCategory.TenK, ResultTimeSeconds: 2790, OverallPlace: 87),
            Rating = new RunRating(4, "Felt strong"),
        };
        return change is null ? v : change(v);
    }

    [Fact]
    public async Task Migrations_seed_the_twenty_run_types_from_the_catalog()
    {
        var cs = await NewDatabaseAsync();
        await using var db = CreateContext(cs, AnonymousCurrentUser.Instance);

        var stored = await db.RunTypes.AsNoTracking().OrderBy(t => t.Id).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(20, stored.Count);
        Assert.Equal(RunTypeCatalog.All.OrderBy(t => t.Id).Select(t => (t.Id, t.Code, t.Category, t.CountsTowardRunningDistance, t.CountsTowardLoad, t.IsRestDay, t.SortOrder)),
            stored.Select(t => (t.Id, t.Code, t.Category, t.CountsTowardRunningDistance, t.CountsTowardLoad, t.IsRestDay, t.SortOrder)));
    }

    [Fact]
    public async Task Host_applies_migrations_on_startup_and_maps_geography_through_its_own_di_registration()
    {
        factory.SkipIfDockerUnavailable();
        var cs = new SqlConnectionStringBuilder(factory.SqlConnectionString) { InitialCatalog = "host_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        await using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:runnersdiary", cs);
            b.UseSetting("Database:MigrateOnStartup", "true");
        });
        using var client = host.CreateClient(); // starts the host, which runs DatabaseMigrator first

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();
        var user = User.CreateGuest(Now);
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(20, await db.RunTypes.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal("geography", await ScalarAsync<string>(cs,
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Runs' AND COLUMN_NAME='Location_Point'"));

        // Anonymous default ICurrentUser: owned data is invisible and cannot be written (the guard throws before any SQL).
        Assert.Empty(await db.Runs.ToListAsync(TestContext.Current.CancellationToken));
        db.Runs.Add(Run.Create(user.Id, SampleRun(), Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        // The same DI registration (options with NetTopologySuite) writes a located run once the caller is explicit, as the seeder does.
        await using var asOwner = new RunnersDiaryDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<RunnersDiaryDbContext>>(), new FixedCurrentUser(user.Id));
        asOwner.Runs.Add(Run.Create(user.Id, SampleRun(), Now));
        await asOwner.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM Runs WHERE Location_Point.STSrid = 4326"));
    }

    [Fact]
    public async Task Model_has_no_pending_changes_after_migrating()
    {
        var cs = await NewDatabaseAsync();
        await using var db = CreateContext(cs, AnonymousCurrentUser.Instance);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Location_point_is_a_geography_column_and_rowversion_is_a_timestamp()
    {
        var cs = await NewDatabaseAsync();

        Assert.Equal("geography", await ScalarAsync<string>(cs,
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Runs' AND COLUMN_NAME='Location_Point'"));
        Assert.Equal("timestamp", await ScalarAsync<string>(cs,
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Runs' AND COLUMN_NAME='RowVersion'"));
        Assert.Equal("date", await ScalarAsync<string>(cs,
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Runs' AND COLUMN_NAME='Date'"));
    }

    [Fact]
    public async Task Runs_are_clustered_on_user_and_date_with_a_non_clustered_primary_key()
    {
        var cs = await NewDatabaseAsync();

        var clusteredColumns = await ScalarAsync<string>(cs, """
            SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('Runs') AND i.type_desc = 'CLUSTERED'
            """);
        var pkType = await ScalarAsync<string>(cs,
            "SELECT type_desc FROM sys.indexes WHERE object_id = OBJECT_ID('Runs') AND is_primary_key = 1");

        Assert.Equal("UserId,Date", clusteredColumns);
        Assert.Equal("NONCLUSTERED", pkType);
    }

    [Fact]
    public async Task Run_round_trips_with_location_race_and_rating_and_stores_a_real_geography_point()
    {
        var cs = await NewDatabaseAsync();
        var user = User.CreateGuest(Now);
        var userContext = new FixedCurrentUser(user.Id, isGuest: true);
        Guid runId;

        await using (var db = CreateContext(cs, userContext))
        {
            db.Users.Add(user);
            var run = Run.Create(user.Id, SampleRun(), Now);
            runId = run.Id;
            db.Runs.Add(run);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = CreateContext(cs, userContext))
        {
            var run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);

            Assert.Equal(new DateOnly(2026, 10, 25), run.Date);
            Assert.Equal(new TimeOnly(9, 0), run.StartTime);
            Assert.Equal(new DateTime(2026, 10, 25, 8, 0, 0, DateTimeKind.Utc), run.StartUtc);
            Assert.Equal(DateTimeKind.Utc, run.CreatedAt.Kind);
            Assert.Equal(165, run.AvgHeartRate);
            Assert.Equal(8, run.Rpe);
            Assert.Equal(new GeoPoint(50.2945, 18.6714), run.Location!.Point);
            Assert.Equal("Park Chrobrego, Gliwice", run.Location.Name);
            Assert.Equal(new RaceDetails("Gliwice 10K", RaceDistanceCategory.TenK, ResultTimeSeconds: 2790, OverallPlace: 87), run.Race);
            Assert.Equal(new RunRating(4, "Felt strong"), run.Rating);
            Assert.NotEmpty(run.RowVersion);
        }

        // The stored value is a real SRID 4326 point with X = longitude, Y = latitude.
        Assert.Equal(4326, await ScalarAsync<int>(cs, "SELECT Location_Point.STSrid FROM Runs"));
        Assert.Equal(50.2945, await ScalarAsync<double>(cs, "SELECT Location_Point.Lat FROM Runs"), 6);
        Assert.Equal(18.6714, await ScalarAsync<double>(cs, "SELECT Location_Point.Long FROM Runs"), 6);
    }

    [Fact]
    public async Task Optional_complex_types_stay_null_when_absent()
    {
        var cs = await NewDatabaseAsync();
        var user = User.CreateGuest(Now);
        var ctx = new FixedCurrentUser(user.Id, isGuest: true);
        var plain = SampleRun(v => v with { RunTypeId = 20, Location = null, Race = null, Rating = null });
        Guid runId;

        await using (var db = CreateContext(cs, ctx))
        {
            db.Users.Add(user);
            var run = Run.Create(user.Id, plain, Now);
            runId = run.Id;
            db.Runs.Add(run);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = CreateContext(cs, ctx))
        {
            var run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);

            Assert.Null(run.Location);
            Assert.Null(run.Race);
            Assert.Null(run.Rating);
        }
    }

    [Fact]
    public async Task Owner_filter_hides_other_users_runs_and_an_anonymous_caller_sees_nothing()
    {
        var cs = await NewDatabaseAsync();
        var a = User.CreateGuest(Now);
        var b = User.CreateGuest(Now);
        Guid runA;

        await using (var db = CreateContext(cs, new FixedCurrentUser(a.Id, true)))
        {
            db.Users.AddRange(a, b);
            var run = Run.Create(a.Id, SampleRun(), Now);
            runA = run.Id;
            db.Runs.Add(run);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var asB = CreateContext(cs, new FixedCurrentUser(b.Id, true)))
        {
            Assert.Empty(await asB.Runs.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Null(await asB.Runs.FirstOrDefaultAsync(r => r.Id == runA, TestContext.Current.CancellationToken));
        }

        await using (var anonymous = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            Assert.Empty(await anonymous.Runs.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Single(await anonymous.Runs.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).ToListAsync(TestContext.Current.CancellationToken));
        }

        await using (var asA = CreateContext(cs, new FixedCurrentUser(a.Id, true)))
        {
            Assert.Single(await asA.Runs.ToListAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Ownership_guard_rejects_writing_another_users_run()
    {
        var cs = await NewDatabaseAsync();
        var a = User.CreateGuest(Now);
        var b = User.CreateGuest(Now);

        await using (var seed = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            seed.Users.AddRange(a, b);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var asA = CreateContext(cs, new FixedCurrentUser(a.Id, true));
        asA.Runs.Add(Run.Create(b.Id, SampleRun(), Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => asA.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Track_processing_columns_exist_with_the_documented_types_and_are_nullable_except_the_flags()
    {
        var cs = await NewDatabaseAsync();
        var columns = new Dictionary<string, (string Type, string Nullable)>
        {
            ["ProcessedBlobName"] = ("varchar", "YES"), ["ProcessorVersion"] = ("tinyint", "YES"), ["ProcessedAt"] = ("datetime2", "YES"),
            ["PointCount"] = ("int", "YES"), ["DistanceMeters"] = ("float", "YES"), ["ElevationGainMeters"] = ("float", "YES"),
            ["ElevationLossMeters"] = ("float", "YES"), ["MinElevationMeters"] = ("float", "YES"), ["MaxElevationMeters"] = ("float", "YES"),
            ["ElapsedSeconds"] = ("int", "YES"), ["MovingSeconds"] = ("int", "YES"), ["StartUtc"] = ("datetime2", "YES"), ["EndUtc"] = ("datetime2", "YES"),
            ["StartPoint"] = ("geography", "YES"), ["FinishPoint"] = ("geography", "YES"), ["WarningFlags"] = ("tinyint", "NO"),
        };

        foreach (var (name, expected) in columns)
        {
            Assert.Equal(
                $"{expected.Type}/{expected.Nullable}",
                await ScalarAsync<string>(cs, $"SELECT DATA_TYPE + '/' + IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='GpxTracks' AND COLUMN_NAME='{name}'"));
        }

        Assert.Equal(1, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_GpxTracks_Processed'"));
    }

    [Fact]
    public async Task A_processed_track_round_trips_and_the_check_constraint_guards_processed_rows_only()
    {
        var cs = await NewDatabaseAsync();
        var user = User.CreateGuest(Now);
        var track = Domain.Gpx.TrackProcessor.Process(Domain.Gpx.GpxReader.Read(
            new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "seed", "demo", "gpx", "gliwice-10k.gpx"))),
            Domain.Gpx.GpxReadLimits.Default, Now).Document!, TestContext.Current.CancellationToken);
        Guid pendingId = Guid.NewGuid(), processedId = Guid.NewGuid();

        await using (var db = CreateContext(cs, new FixedCurrentUser(user.Id, true)))
        {
            db.Users.Add(user);
            var run = Run.Create(user.Id, SampleRun(), Now);
            var other = Run.Create(user.Id, SampleRun(), Now);
            db.Runs.AddRange(run, other);
            db.GpxTracks.Add(GpxTrack.CreatePending(pendingId, other.Id, user.Id, GpxTrackKind.PlannedCourse, "a.gpx", 10, new byte[32], "users/x/a.gpx", Now));
            db.GpxTracks.Add(GpxTrack.CreateProcessed(
                processedId, run.Id, user.Id, GpxTrackKind.RecordedTrack, "race.gpx", 1234, new byte[32], "users/x/original.gpx", "users/x/track.json", track, Now));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = CreateContext(cs, new FixedCurrentUser(user.Id, true)))
        {
            var stored = await db.GpxTracks.AsNoTracking().SingleAsync(t => t.Id == processedId, TestContext.Current.CancellationToken);
            Assert.Equal(GpxTrackStatus.Processed, stored.Status);
            Assert.Equal("users/x/track.json", stored.ProcessedBlobName);
            Assert.Equal((byte)1, stored.ProcessorVersion);
            Assert.Equal(track.Summary.DistanceMeters, stored.DistanceMeters);
            Assert.Equal(track.Summary.PointCount, stored.PointCount);
            Assert.Equal(track.Summary.ElapsedSeconds, stored.ElapsedSeconds);
            Assert.Equal(DateTimeKind.Utc, stored.StartUtc!.Value.Kind);
            Assert.Equal(track.Summary.StartLatitude, stored.StartPoint!.Value.Latitude, 6);
            Assert.Equal(track.Summary.FinishLongitude, stored.FinishPoint!.Value.Longitude, 6);
            Assert.Equal(Domain.Gpx.TrackWarnings.None, stored.WarningFlags);

            var pending = await db.GpxTracks.AsNoTracking().SingleAsync(t => t.Id == pendingId, TestContext.Current.CancellationToken);
            Assert.Equal(GpxTrackStatus.Pending, pending.Status);
            Assert.Null(pending.DistanceMeters);
        }

        // A Processed row without its summary is rejected by the database; the same row stays valid while Pending.
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var update = new SqlCommand($"UPDATE GpxTracks SET Status = 1 WHERE Id = '{pendingId}'", connection);
        await Assert.ThrowsAsync<SqlException>(() => update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_a_user_cascades_to_runs_and_tracks_and_the_run_type_fk_restricts()
    {
        var cs = await NewDatabaseAsync();
        var user = User.CreateGuest(Now);
        var ctx = new FixedCurrentUser(user.Id, true);

        await using (var db = CreateContext(cs, ctx))
        {
            db.Users.Add(user);
            var run = Run.Create(user.Id, SampleRun(), Now);
            db.Runs.Add(run);
            var trackId = Guid.NewGuid();
            db.GpxTracks.Add(GpxTrack.CreatePending(
                trackId, run.Id, user.Id, GpxTrackKind.RecordedTrack, "race.gpx", 1234, new byte[32],
                $"users/{user.Id:N}/tracks/{trackId:N}/original.gpx", Now));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM GpxTracks"));

        await using (var cleanup = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            await cleanup.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM Runs"));
        Assert.Equal(0, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM GpxTracks"));
        Assert.Equal(20, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM RunTypes"));
    }

    [Fact]
    public async Task Ownership_guard_rejects_any_owned_write_from_a_context_without_a_caller()
    {
        var cs = await NewDatabaseAsync();
        var user = User.CreateGuest(Now);
        await using (var seed = CreateContext(cs, new FixedCurrentUser(user.Id, true)))
        {
            seed.Users.Add(user);
            seed.Runs.Add(Run.Create(user.Id, SampleRun(), Now));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var anonymous = CreateContext(cs, AnonymousCurrentUser.Instance);
        anonymous.Runs.Add(Run.Create(user.Id, SampleRun(), Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => anonymous.SaveChangesAsync(TestContext.Current.CancellationToken));
        anonymous.ChangeTracker.Clear();

        var existing = await anonymous.Runs.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).SingleAsync(TestContext.Current.CancellationToken);
        anonymous.Remove(existing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => anonymous.SaveChangesAsync(TestContext.Current.CancellationToken));
        anonymous.ChangeTracker.Clear();

        // Non-owned entities and bulk deletes on Users (what provisioning and guest cleanup do) still work without a caller.
        anonymous.Users.Add(User.CreateEntra(Guid.NewGuid(), Guid.NewGuid(), "Ada", Now));
        await anonymous.SaveChangesAsync(TestContext.Current.CancellationToken);
        await anonymous.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM Runs"));
    }

    [Fact]
    public async Task Track_foreign_key_is_composite_so_a_track_cannot_point_at_another_users_run()
    {
        var cs = await NewDatabaseAsync();
        var a = User.CreateGuest(Now);
        var b = User.CreateGuest(Now);
        var runOfA = Run.Create(a.Id, SampleRun(), Now);
        await using (var seed = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            seed.Users.AddRange(a, b);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var asA = CreateContext(cs, new FixedCurrentUser(a.Id, true)))
        {
            asA.Runs.Add(runOfA);
            await asA.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // B writes a track that names its own UserId (so the application guard passes) but A's run: only the database can stop it.
        await using var asB = CreateContext(cs, new FixedCurrentUser(b.Id, true));
        var trackId = Guid.NewGuid();
        asB.GpxTracks.Add(GpxTrack.CreatePending(
            trackId, runOfA.Id, b.Id, GpxTrackKind.RecordedTrack, "x.gpx", 10, new byte[32], $"users/{b.Id:N}/tracks/{trackId:N}/original.gpx", Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => asB.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, await ScalarAsync<int>(cs,
            "SELECT COUNT(*) FROM sys.foreign_key_columns fkc JOIN sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id " +
            "WHERE fk.parent_object_id = OBJECT_ID('GpxTracks') AND fk.name = 'FK_GpxTracks_Runs_RunId_UserId' AND fkc.parent_column_id = " +
            "(SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('GpxTracks') AND name = 'UserId')"));
    }

    [Fact]
    public async Task Users_table_enforces_kind_consistency_and_unique_external_identity()
    {
        var cs = await NewDatabaseAsync();
        var tenant = Guid.NewGuid();
        var oid = Guid.NewGuid();

        await using (var db = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            db.Users.Add(User.CreateEntra(tenant, oid, "Ada", Now));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            db.Users.Add(User.CreateEntra(tenant, oid, "Ada again", Now));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using (var db = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            db.Users.AddRange(User.CreateGuest(Now), User.CreateGuest(Now));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(3, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM Users"));
        await Assert.ThrowsAsync<SqlException>(() => ScalarAsync<int>(cs,
            "INSERT INTO Users (Id, Kind, TimeZoneId, TimeZoneIsDefault, UnitSystem, CreatedAt) VALUES (NEWID(), 1, 'Etc/UTC', 0, 0, SYSUTCDATETIME()); SELECT 1"));
    }
}
