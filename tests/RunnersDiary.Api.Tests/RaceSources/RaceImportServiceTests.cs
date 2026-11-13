using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.RaceSources;
using RunnersDiary.Infrastructure.RaceSources.Demo;
using static RunnersDiary.Api.Tests.RaceSources.ImportHarness;

namespace RunnersDiary.Api.Tests.RaceSources;

/// <summary>The import service against real SQL Server: transactional upsert, lock, removal, duplicates, retention purge (ADR 0036 §2-4, ADR 0038 §1).</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class RaceImportServiceTests(ContainersApiFactory factory)
{
    private static readonly DateOnly Today = new(2026, 11, 13);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_demo_source_imports_the_catalogue_once_and_a_second_run_has_no_changes()
    {
        await using var h = await CreateAsync(factory);
        var service = h.Service(DemoOptions(), new DemoRaceSource(h.DemoCatalogue));

        var first = await service.RunAsync(Ct);
        h.Clock.Advance(TimeSpan.FromHours(1)); // same day: the demo dates are relative to today, so a later day would legitimately move them
        var second = await service.RunAsync(Ct);

        Assert.Equal(RaceImportOutcome.Completed, first.Outcome);
        var firstSummary = Assert.Single(first.Sources);
        Assert.Equal(40, firstSummary.New);
        Assert.Equal(0, firstSummary.Updated);
        Assert.Equal(RaceImportOutcome.Completed, second.Outcome);
        var secondSummary = Assert.Single(second.Sources);
        Assert.Equal((0, 0, 0, 0, 0), (secondSummary.New, secondSummary.Updated, secondSummary.DateChanged, secondSummary.Removed, secondSummary.Restored));
        Assert.Equal(40, (await h.RacesAsync()).Count);
    }

    [Fact]
    public async Task A_disabled_import_opens_no_sql_connection()
    {
        await using var h = await CreateAsync(factory);
        var scripted = new ScriptedSource(RaceSource.Demo, _ => Scan([]));
        var before = h.Interceptor.OpenedCount;

        var global = await h.Service(new RaceImportOptions { Enabled = false, Sources = { Demo = { Enabled = true } } }, scripted).RunAsync(Ct);
        var noSource = await h.Service(new RaceImportOptions { Enabled = true }, scripted).RunAsync(Ct);

        Assert.Equal(RaceImportOutcome.Disabled, global.Outcome);
        Assert.Equal(RaceImportOutcome.Disabled, noSource.Outcome);
        Assert.Equal(0, scripted.Scans);
        Assert.Equal(before, h.Interceptor.OpenedCount);
    }

    [Fact]
    public async Task While_another_session_holds_the_lock_the_run_does_nothing_and_after_its_release_it_runs()
    {
        await using var h = await CreateAsync(factory);
        var source = new ScriptedSource(RaceSource.Demo, _ => Scan([Facts("a", Today.AddDays(10))]));
        var service = h.Service(DemoOptions(), source);

        await using (var other = new SqlConnection(h.ConnectionString))
        {
            await other.OpenAsync(Ct);
            await using var take = new SqlCommand(
                $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'{RaceImportService.LockResource}', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0; SELECT @r;", other);
            Assert.True((int)(await take.ExecuteScalarAsync(Ct))! >= 0);

            var locked = await service.RunAsync(Ct);

            Assert.Equal(RaceImportOutcome.Locked, locked.Outcome);
            Assert.Equal(0, locked.ExitCode);
            Assert.Equal(0, source.Scans);
            Assert.Empty(await h.RacesAsync());

            // A pooled connection keeps its session lock until it is reused; release it like a finished job does.
            await using var release = new SqlCommand($"EXEC sp_releaseapplock @Resource = N'{RaceImportService.LockResource}', @LockOwner = N'Session';", other);
            await release.ExecuteNonQueryAsync(Ct);
        }

        var after = await service.RunAsync(Ct);
        Assert.Equal(RaceImportOutcome.Completed, after.Outcome);
        Assert.Single(await h.RacesAsync());
    }

    [Fact]
    public async Task The_lock_is_released_after_a_run_so_the_next_run_is_not_blocked()
    {
        await using var h = await CreateAsync(factory);
        var service = h.Service(DemoOptions(), new ScriptedSource(RaceSource.Demo, _ => Scan([Facts("a", Today.AddDays(10))])));

        Assert.Equal(RaceImportOutcome.Completed, (await service.RunAsync(Ct)).Outcome);
        Assert.Equal(RaceImportOutcome.Completed, (await service.RunAsync(Ct)).Outcome);
    }

    [Fact]
    public async Task A_race_missing_from_complete_scans_is_removed_after_two_misses_and_restored_when_it_reappears()
    {
        await using var h = await CreateAsync(factory);
        var a = Facts("a", Today.AddDays(30));
        var b = Facts("b", Today.AddDays(31), "Giraffe b", new GeoPoint(51.1, 17.0), "Wroclaw");
        var listed = new List<RaceFacts> { a, b };
        var service = h.Service(DemoOptions(), new ScriptedSource(RaceSource.Demo, _ => Scan(listed)));

        await service.RunAsync(Ct);
        listed.Remove(b);

        var miss1 = (await service.RunAsync(Ct)).Sources.Single();
        var afterOne = (await h.RacesAsync()).Single(r => r.ExternalId == "b");
        var miss2 = (await service.RunAsync(Ct)).Sources.Single();
        var afterTwo = (await h.RacesAsync()).Single(r => r.ExternalId == "b");
        listed.Add(b);
        var back = (await service.RunAsync(Ct)).Sources.Single();
        var restored = (await h.RacesAsync()).Single(r => r.ExternalId == "b");

        Assert.Equal(1, afterOne.MissedScans);
        Assert.Null(afterOne.RemovedUtc);
        Assert.Equal(0, miss1.Removed);
        Assert.Equal(2, afterTwo.MissedScans);
        Assert.NotNull(afterTwo.RemovedUtc);
        Assert.Equal(1, miss2.Removed);
        Assert.Equal(1, back.Restored);
        Assert.Equal((0, null), ((int)restored.MissedScans, restored.RemovedUtc));
    }

    [Fact]
    public async Task An_incomplete_scan_changes_no_missed_counter()
    {
        await using var h = await CreateAsync(factory);
        var a = Facts("a", Today.AddDays(30));
        var b = Facts("b", Today.AddDays(31), "Giraffe b", new GeoPoint(51.1, 17.0), "Wroclaw");
        var scans = new Queue<RaceScanResult>([Scan([a, b]), Scan([a], complete: false, outcome: RaceScanOutcome.StoppedByServer), Scan([a], complete: false, outcome: RaceScanOutcome.StoppedByServer), Scan([a], complete: false, outcome: RaceScanOutcome.StoppedByServer)]);
        var service = h.Service(DemoOptions(), new ScriptedSource(RaceSource.Demo, _ => scans.Dequeue()));

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(RaceImportOutcome.Completed, (await service.RunAsync(Ct)).Outcome);
        }

        var stored = (await h.RacesAsync()).Single(r => r.ExternalId == "b");
        Assert.Equal((0, null), ((int)stored.MissedScans, stored.RemovedUtc));
    }

    [Fact]
    public async Task A_changed_date_is_updated_in_place_and_the_previous_date_is_recorded()
    {
        await using var h = await CreateAsync(factory);
        var date = Today.AddDays(30);
        var current = Facts("a", date);
        var service = h.Service(DemoOptions(), new ScriptedSource(RaceSource.Demo, _ => Scan([current])));

        await service.RunAsync(Ct);
        current = current with { Date = date.AddDays(7) };
        var summary = (await service.RunAsync(Ct)).Sources.Single();

        var stored = Assert.Single(await h.RacesAsync());
        Assert.Equal(1, summary.Updated);
        Assert.Equal(1, summary.DateChanged);
        Assert.Equal(date, stored.PreviousDate);
        Assert.Equal(date.AddDays(7), stored.Date);
    }

    [Fact]
    public async Task Detail_fetch_validators_are_stored_on_the_race()
    {
        await using var h = await CreateAsync(factory);
        var service = h.Service(DemoOptions(), new ScriptedSource(RaceSource.Demo, _ => Scan([Facts("a", Today.AddDays(30))], fetches: new DetailFetchRecord("a", "\"v1\"", "Fri, 13 Nov 2026 03:00:00 GMT"))));

        await service.RunAsync(Ct);

        var stored = Assert.Single(await h.RacesAsync());
        Assert.Equal("\"v1\"", stored.DetailETag);
        Assert.Equal("Fri, 13 Nov 2026 03:00:00 GMT", stored.DetailLastModified);
        Assert.NotNull(stored.DetailFetchedUtc);
    }

    [Fact]
    public async Task A_known_race_context_carries_the_stored_detail_facts_and_validators()
    {
        await using var h = await CreateAsync(factory);
        RaceScanContext? seen = null;
        var phase = 0;
        var service = h.Service(DemoOptions(), new ScriptedSource(RaceSource.Demo, c =>
        {
            seen = c;
            return phase++ == 0 ? Scan([Facts("a", Today.AddDays(30)) with { DistancesMeters = [5_000, 10_000], SignupUrl = "https://zapisy.example/a" }], fetches: new DetailFetchRecord("a", "\"v1\"", null)) : Scan([]);
        }));

        await service.RunAsync(Ct);
        await service.RunAsync(Ct);

        var known = Assert.Single(seen!.Known).Value;
        Assert.Equal([5_000, 10_000], known.Facts.DistancesMeters);
        Assert.Equal("https://zapisy.example/a", known.Facts.SignupUrl);
        Assert.Equal("\"v1\"", known.DetailETag);
        Assert.NotNull(known.DetailFetchedUtc);
        Assert.Equal(Today, seen.Today);
    }

    [Fact]
    public async Task A_layout_change_writes_nothing_purges_nothing_and_fails_the_run()
    {
        await using var h = await CreateAsync(factory);
        var old = Race.Create(Facts("old", Today.AddDays(-400)), Start());
        await h.AddRacesAsync(old);
        var service = h.Service(DemoOptions(), new ScriptedSource(RaceSource.Demo, _ => RaceScanResult.Failed(RaceScanOutcome.LayoutChanged)));

        var result = await service.RunAsync(Ct);

        Assert.Equal(RaceImportOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["old"], (await h.RacesAsync()).Select(r => r.ExternalId));
    }

    [Fact]
    public async Task A_source_that_throws_fails_the_run_but_the_other_source_still_imports()
    {
        await using var h = await CreateAsync(factory);
        var failing = new ScriptedSource(RaceSource.RunningLife, _ => throw new InvalidOperationException("boom"));
        var working = new ScriptedSource(RaceSource.Demo, _ => Scan([Facts("a", Today.AddDays(30))]));
        var options = new RaceImportOptions { Enabled = true, Sources = { Demo = { Enabled = true }, RunningLife = { Enabled = true } } };

        var result = await h.Service(options, failing, working).RunAsync(Ct);

        Assert.Equal(RaceImportOutcome.Failed, result.Outcome);
        Assert.Equal(2, result.Sources.Count);
        Assert.Single(await h.RacesAsync());
    }

    [Fact]
    public async Task The_duplicate_matcher_marks_the_younger_row_of_a_same_day_same_name_pair_and_clears_it_when_they_diverge()
    {
        await using var h = await CreateAsync(factory);
        var date = Today.AddDays(40);
        var first = Facts("one", date, "Bieg Alfa 2027");
        var second = Facts("two", date, "Alfa", new GeoPoint(50.2950, 18.6720));
        var other = Facts("far", date, "Alfa", new GeoPoint(52.2297, 21.0122), "Warszawa");
        var listed = new List<RaceFacts> { first };
        var service = h.Service(DemoOptions(), new ScriptedSource(RaceSource.Demo, _ => Scan(listed)));

        await service.RunAsync(Ct);
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        listed.AddRange([second, other]);
        var summary = (await service.RunAsync(Ct)).Sources.Single();

        var races = await h.RacesAsync();
        var one = races.Single(r => r.ExternalId == "one");
        var two = races.Single(r => r.ExternalId == "two");
        Assert.Null(one.DuplicateOfRaceId);
        Assert.Equal(one.Id, two.DuplicateOfRaceId);
        Assert.Null(races.Single(r => r.ExternalId == "far").DuplicateOfRaceId);
        Assert.Equal(1, summary.Duplicates);

        listed[1] = second with { Date = date.AddDays(1) };
        await service.RunAsync(Ct);
        Assert.Null((await h.RacesAsync()).Single(r => r.ExternalId == "two").DuplicateOfRaceId);
    }

    [Fact]
    public async Task The_purge_deletes_only_this_sources_races_older_than_the_retention_and_spares_canonical_rows_that_others_still_reference()
    {
        await using var h = await CreateAsync(factory);
        var ancient = Race.Create(Facts("ancient", Today.AddDays(-300)), Start());
        var recent = Race.Create(Facts("recent", Today.AddDays(-100)), Start());
        var canonical = Race.Create(Facts("canonical", Today.AddDays(-250), "Delta"), Start());
        var survivor = Race.Create(Facts("survivor", Today.AddDays(-20), "Delta"), Start());
        survivor.SetDuplicateOf(canonical.Id);
        var pairOld = Race.Create(Facts("pair-old", Today.AddDays(-260), "Echo"), Start());
        var pairOlder = Race.Create(Facts("pair-older", Today.AddDays(-261), "Echo"), Start());
        pairOlder.SetDuplicateOf(pairOld.Id);
        var otherSource = Race.Create(Facts("rl", Today.AddDays(-300)) with { Source = RaceSource.RunningLife, SourceUrl = "https://running.life/pl/wydarzenie/rl" }, Start());
        await h.AddRacesAsync(ancient, recent, canonical, survivor, pairOld, pairOlder, otherSource);
        // The listing still carries the surviving and the recent race so that nothing counts as missed.
        var service = h.Service(DemoOptions(o => o.RetainPastDays = 180), new ScriptedSource(RaceSource.Demo, _ => Scan([])));

        var summary = (await service.RunAsync(Ct)).Sources.Single();

        var left = (await h.RacesAsync()).Select(r => r.ExternalId).ToList();
        Assert.Equal(["canonical", "recent", "rl", "survivor"], left);
        Assert.Equal(3, summary.Purged); // ancient, pair-old, pair-older; "canonical" is spared, "rl" belongs to another source
    }

    [Fact]
    public async Task The_purge_runs_only_after_a_completed_scan()
    {
        await using var h = await CreateAsync(factory);
        await h.AddRacesAsync(Race.Create(Facts("ancient", Today.AddDays(-300)), Start()));
        var service = h.Service(DemoOptions(), new ScriptedSource(RaceSource.Demo, _ => Scan([Facts("a", Today.AddDays(30))], complete: false, outcome: RaceScanOutcome.StoppedByServer)));

        var summary = (await service.RunAsync(Ct)).Sources.Single();

        Assert.Equal(0, summary.Purged);
        Assert.Contains("ancient", (await h.RacesAsync()).Select(r => r.ExternalId));
    }

    [Fact]
    public async Task Purging_a_race_cascades_to_distances_and_states_and_unlinks_runs()
    {
        await using var h = await CreateAsync(factory);
        var user = User.CreateGuest(Start());
        await using (var db = Context(h.ConnectionString, new FixedCurrentUser(user.Id, isGuest: true)))
        {
            db.Users.Add(user);
            await db.SaveChangesAsync(Ct);
        }

        var old = Race.Create(Facts("old", Today.AddDays(-300)), Start());
        await h.AddRacesAsync(old);
        Guid runId;
        await using (var db = Context(h.ConnectionString, new FixedCurrentUser(user.Id, isGuest: true)))
        {
            var run = Run.Create(user.Id, new RunValues
            {
                Date = Today.AddDays(-300),
                TimeZoneId = "Europe/Warsaw",
                Status = RunStatus.Planned,
                RunTypeId = 1,
                DistanceMeters = 10_000,
                Race = new RaceDetails("Old", RaceDistanceCategory.TenK),
            }, Start());
            run.LinkToCatalogRace(old.Id);
            runId = run.Id;
            db.Runs.Add(run);
            db.RaceProposalStates.Add(RaceProposalState.Create(user.Id, old.Id, RaceProposalMark.Interested, old.Date, Start()));
            await db.SaveChangesAsync(Ct);
        }

        var summary = (await h.Service(DemoOptions(), new ScriptedSource(RaceSource.Demo, _ => Scan([]))).RunAsync(Ct)).Sources.Single();

        Assert.Equal(1, summary.Purged);
        await using var asUser = Context(h.ConnectionString, new FixedCurrentUser(user.Id, isGuest: true));
        Assert.Null((await asUser.Runs.AsNoTracking().SingleAsync(r => r.Id == runId, Ct)).CatalogRaceId);
        Assert.Empty(await asUser.RaceProposalStates.AsNoTracking().ToListAsync(Ct));
        Assert.Empty(await h.RacesAsync());
    }

    private static DateTimeOffset Start() => ImportHarness.Start;
}
