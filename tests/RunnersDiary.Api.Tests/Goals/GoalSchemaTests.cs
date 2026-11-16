using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>
/// Applies the real migrations (including <c>TrainingGoals</c>) to a fresh SQL Server database and checks keys, delete actions, the composite
/// "same owner" FKs, checks and the guest-deletion cascade with mutual Goal-Run links (the ⚠ of ADR 0039 §3).
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GoalSchemaTests(ContainersApiFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> NewDatabaseAsync(string? targetMigration = null)
    {
        factory.SkipIfDockerUnavailable();
        var cs = new SqlConnectionStringBuilder(factory.SqlConnectionString) { InitialCatalog = "goals_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        await using var db = CreateContext(cs, AnonymousCurrentUser.Instance);
        if (targetMigration is null)
        {
            await db.Database.MigrateAsync(Ct);
        }
        else
        {
            await db.GetService<IMigrator>().MigrateAsync(targetMigration, Ct);
        }

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

    private static async Task ExecuteAsync(string cs, string sql)
    {
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<User> AddUserAsync(string cs)
    {
        var user = User.CreateGuest(Now);
        await using var db = CreateContext(cs, new FixedCurrentUser(user.Id, isGuest: true));
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        return user;
    }

    private static GoalValues GoalValuesFor(DateOnly? date = null) =>
        new(GoalType.Distance, "Test goal", date ?? Today.AddDays(60), 10_000, null, null, Surface.Road, null, null, null, null, GoalStatus.Active);

    private static RunValues PlannedRun(DateOnly? date = null) => new()
    {
        Date = date ?? Today.AddDays(60),
        TimeZoneId = "Europe/Warsaw",
        Status = RunStatus.Planned,
        RunTypeId = 1,
        DistanceMeters = 10_000,
        Race = new RaceDetails("Test race", RaceDistanceCategory.TenK),
    };

    private static PlannedSession Session(DateOnly date) =>
        new(date, 20, 8_000, 2_700, TrainingIntensity.Easy, 330, 360, "Easy run.", TrainingPhase.Base);

    /// <summary>A goal with two suggestions, a run created from it (<c>Runs.GoalId</c>) and the goal pointing back at a race run (<c>Goals.RaceRunId</c>).</summary>
    private static async Task<(Guid GoalId, Guid AcceptedRunId, Guid RaceRunId)> SeedMutuallyLinkedAsync(string cs, User user)
    {
        await using var db = CreateContext(cs, new FixedCurrentUser(user.Id, isGuest: true));
        var goal = Goal.Create(user.Id, GoalValuesFor(), Today, null, Now);
        db.Goals.Add(goal);
        await db.SaveChangesAsync(Ct);

        var accepted = Run.Create(user.Id, new RunValues { Date = Today.AddDays(10), TimeZoneId = "Europe/Warsaw", Status = RunStatus.Planned, RunTypeId = 20, DistanceMeters = 8_000 }, Now);
        accepted.LinkToGoal(goal.Id);
        var raceRun = Run.Create(user.Id, PlannedRun(goal.TargetDate), Now);
        db.Runs.AddRange(accepted, raceRun);
        db.TrainingSuggestions.AddRange(
            TrainingSuggestion.Create(user.Id, goal.Id, Guid.NewGuid(), Session(Today.AddDays(11)), Now),
            TrainingSuggestion.Create(user.Id, goal.Id, Guid.NewGuid(), Session(Today.AddDays(12)), Now));
        await db.SaveChangesAsync(Ct);

        await db.Goals.Where(g => g.Id == goal.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.RaceRunId, raceRun.Id), Ct);
        return (goal.Id, accepted.Id, raceRun.Id);
    }

    [Fact]
    public async Task The_migration_creates_the_documented_keys_indexes_and_delete_actions()
    {
        var cs = await NewDatabaseAsync();

        Assert.Equal("NONCLUSTERED", await ScalarAsync<string>(cs, "SELECT type_desc FROM sys.indexes WHERE object_id = OBJECT_ID('Goals') AND is_primary_key = 1"));
        Assert.Equal("NONCLUSTERED", await ScalarAsync<string>(cs, "SELECT type_desc FROM sys.indexes WHERE object_id = OBJECT_ID('TrainingSuggestions') AND is_primary_key = 1"));
        Assert.Equal("UserId,TargetDate", await ScalarAsync<string>(cs, """
            SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.name = 'CX_Goals_User_TargetDate' AND i.type_desc = 'CLUSTERED' AND i.object_id = OBJECT_ID('Goals')
            """));
        Assert.Equal("UserId,Date", await ScalarAsync<string>(cs, """
            SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.name = 'CX_TrainingSuggestions_User_Date' AND i.type_desc = 'CLUSTERED' AND i.object_id = OBJECT_ID('TrainingSuggestions')
            """));
        Assert.Equal(1, await ScalarAsync<int>(cs, "SELECT CAST(is_unique AS int) FROM sys.indexes WHERE name = 'UX_TrainingSuggestions_Goal_Date'"));
        Assert.Equal(1, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'AK_Goals_Id_UserId' AND type = 'UQ'"));
        Assert.Equal("([GoalId] IS NOT NULL)", await ScalarAsync<string>(cs, "SELECT filter_definition FROM sys.indexes WHERE name = 'IX_Runs_GoalId_UserId'"));
        Assert.Equal("([RaceRunId] IS NOT NULL)", await ScalarAsync<string>(cs, "SELECT filter_definition FROM sys.indexes WHERE name = 'IX_Goals_RaceRunId_UserId'"));

        Assert.Equal("CASCADE", await ScalarAsync<string>(cs, "SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE name = 'FK_Goals_Users_UserId'"));
        Assert.Equal("CASCADE", await ScalarAsync<string>(cs, "SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE name = 'FK_TrainingSuggestions_Goals_GoalId_UserId'"));
        Assert.Equal("NO_ACTION", await ScalarAsync<string>(cs, "SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE name = 'FK_Goals_Runs_RaceRunId_UserId'"));
        Assert.Equal("NO_ACTION", await ScalarAsync<string>(cs, "SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE name = 'FK_Runs_Goals_GoalId_UserId'"));
        Assert.Equal("NO_ACTION", await ScalarAsync<string>(cs, "SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE name = 'FK_TrainingSuggestions_RunTypes_RunTypeId'"));
        // The single cascade path to suggestions is through Goals: no FK from TrainingSuggestions to Users.
        Assert.Equal(0, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('TrainingSuggestions') AND referenced_object_id = OBJECT_ID('Users')"));

        Assert.Equal("tinyint", await ScalarAsync<string>(cs, "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AiGenerations' AND COLUMN_NAME='Feature'"));
        Assert.Contains("(0)", await ScalarAsync<string>(cs, "SELECT COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AiGenerations' AND COLUMN_NAME='Feature'"));
        Assert.Equal("geography", await ScalarAsync<string>(cs, "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Goals' AND COLUMN_NAME='Location_Point'"));

        await using var db = CreateContext(cs, AnonymousCurrentUser.Instance);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    /// <summary>The ⚠ of ADR 0039 §3: SQL Server accepts deleting a user whose goal links a run and whose run links the goal; no fallback is needed.</summary>
    [Fact]
    public async Task Deleting_a_guest_with_mutual_goal_and_run_links_removes_everything_in_one_statement()
    {
        var cs = await NewDatabaseAsync();
        var guest = await AddUserAsync(cs);
        var bystander = await AddUserAsync(cs);
        await SeedMutuallyLinkedAsync(cs, guest);
        await SeedMutuallyLinkedAsync(cs, bystander);
        Assert.Equal(2, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM Runs WHERE UserId = '{guest.Id}'"));
        Assert.Equal(1, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM Goals WHERE UserId = '{guest.Id}' AND RaceRunId IS NOT NULL"));
        Assert.Equal(1, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM Runs WHERE UserId = '{guest.Id}' AND GoalId IS NOT NULL"));

        // What guest cleanup does: one ExecuteDelete on Users from an anonymous context.
        await using (var job = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            Assert.Equal(1, await job.Users.Where(u => u.Id == guest.Id && u.Kind == UserKind.Guest).ExecuteDeleteAsync(Ct));
        }

        Assert.Equal(0, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM Goals WHERE UserId = '{guest.Id}'"));
        Assert.Equal(0, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM Runs WHERE UserId = '{guest.Id}'"));
        Assert.Equal(0, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM TrainingSuggestions WHERE UserId = '{guest.Id}'"));
        Assert.Equal(1, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM Goals WHERE UserId = '{bystander.Id}'"));
        Assert.Equal(2, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM Runs WHERE UserId = '{bystander.Id}'"));
        Assert.Equal(2, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM TrainingSuggestions WHERE UserId = '{bystander.Id}'"));
    }

    [Fact]
    public async Task Deleting_a_goal_or_a_run_that_is_still_linked_is_refused_until_the_application_clears_the_link()
    {
        var cs = await NewDatabaseAsync();
        var user = await AddUserAsync(cs);
        var (goalId, acceptedRunId, raceRunId) = await SeedMutuallyLinkedAsync(cs, user);
        await using var db = CreateContext(cs, new FixedCurrentUser(user.Id, isGuest: true));

        // NO ACTION: the database refuses both deletes while a link exists, so the application must clear them (ADR 0039 §3).
        await Assert.ThrowsAsync<SqlException>(() => db.Goals.Where(g => g.Id == goalId).ExecuteDeleteAsync(Ct));
        await Assert.ThrowsAsync<SqlException>(() => db.Runs.Where(r => r.Id == raceRunId).ExecuteDeleteAsync(Ct));

        // Run delete: clear Goals.RaceRunId first; the goal stays.
        await db.Goals.Where(g => g.RaceRunId == raceRunId).ExecuteUpdateAsync(s => s.SetProperty(g => g.RaceRunId, (Guid?)null), Ct);
        Assert.Equal(1, await db.Runs.Where(r => r.Id == raceRunId).ExecuteDeleteAsync(Ct));
        Assert.True(await db.Goals.AnyAsync(g => g.Id == goalId, Ct));

        // Goal delete: clear Runs.GoalId first; the suggestions cascade, the accepted run stays.
        await db.Runs.Where(r => r.GoalId == goalId).ExecuteUpdateAsync(s => s.SetProperty(r => r.GoalId, (Guid?)null), Ct);
        Assert.Equal(1, await db.Goals.Where(g => g.Id == goalId).ExecuteDeleteAsync(Ct));
        Assert.Equal(0, await db.TrainingSuggestions.CountAsync(Ct));
        var survivor = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == acceptedRunId, Ct);
        Assert.Null(survivor.GoalId);
    }

    [Fact]
    public async Task The_composite_foreign_keys_enforce_the_same_owner()
    {
        var cs = await NewDatabaseAsync();
        var alice = await AddUserAsync(cs);
        var bob = await AddUserAsync(cs);
        var (aliceGoal, _, _) = await SeedMutuallyLinkedAsync(cs, alice);
        var (_, _, bobRaceRun) = await SeedMutuallyLinkedAsync(cs, bob);

        // Bob's race run as Alice's RaceRunId, Alice's goal as the goal of Bob's suggestion, and Alice's goal on Bob's run: all refused.
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(cs, $"UPDATE Goals SET RaceRunId = '{bobRaceRun}' WHERE Id = '{aliceGoal}'"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(cs, $"UPDATE Runs SET GoalId = '{aliceGoal}' WHERE Id = '{bobRaceRun}'"));
        await using var db = CreateContext(cs, new FixedCurrentUser(bob.Id, isGuest: true));
        db.TrainingSuggestions.Add(TrainingSuggestion.Create(bob.Id, aliceGoal, Guid.NewGuid(), Session(Today.AddDays(20)), Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task One_suggestion_per_goal_and_day()
    {
        var cs = await NewDatabaseAsync();
        var user = await AddUserAsync(cs);
        var (goalId, _, _) = await SeedMutuallyLinkedAsync(cs, user);
        await using var db = CreateContext(cs, new FixedCurrentUser(user.Id, isGuest: true));

        db.TrainingSuggestions.Add(TrainingSuggestion.Create(user.Id, goalId, Guid.NewGuid(), Session(Today.AddDays(11)), Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Owner_filters_hide_foreign_goals_and_suggestions_and_the_guard_rejects_foreign_writes()
    {
        var cs = await NewDatabaseAsync();
        var alice = await AddUserAsync(cs);
        var bob = await AddUserAsync(cs);
        var (aliceGoal, _, _) = await SeedMutuallyLinkedAsync(cs, alice);

        await using var asBob = CreateContext(cs, new FixedCurrentUser(bob.Id, isGuest: true));
        Assert.Empty(await asBob.Goals.ToListAsync(Ct));
        Assert.Empty(await asBob.TrainingSuggestions.ToListAsync(Ct));
        Assert.Equal(0, await asBob.Goals.Where(g => g.Id == aliceGoal).ExecuteDeleteAsync(Ct));

        asBob.Goals.Add(Goal.Create(alice.Id, GoalValuesFor(), Today, null, Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => asBob.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Check_constraints_protect_the_goal_and_suggestion_invariants()
    {
        var cs = await NewDatabaseAsync();
        var user = await AddUserAsync(cs);
        var (goalId, _, _) = await SeedMutuallyLinkedAsync(cs, user);

        string Insert(int type, string duration, string distance = "10000", string status = "0") => $"""
            INSERT INTO Goals (Id, UserId, [Type], Title, TargetDate, DistanceMeters, TargetDurationSeconds, [Status], PlanHorizonCapped, PlanOutdated, CreatedAt, UpdatedAt)
            VALUES (NEWID(), '{user.Id}', {type}, 'x', '2027-01-01', {distance}, {duration}, {status}, 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME())
            """;

        await ExecuteAsync(cs, Insert(0, "NULL"));
        await ExecuteAsync(cs, Insert(1, "3000"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(cs, Insert(1, "NULL"))); // Performance needs a target
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(cs, Insert(2, "3000"))); // Distance has none
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(cs, Insert(3, "NULL"))); // unknown type
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(cs, Insert(0, "NULL", distance: "999")));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(cs, Insert(0, "NULL", status: "3")));

        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(cs,
            $"UPDATE TrainingSuggestions SET TargetPaceMinSecondsPerKm = 300, TargetPaceMaxSecondsPerKm = NULL WHERE GoalId = '{goalId}'"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(cs,
            $"UPDATE TrainingSuggestions SET TargetPaceMinSecondsPerKm = 400, TargetPaceMaxSecondsPerKm = 300 WHERE GoalId = '{goalId}'"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(cs, $"UPDATE TrainingSuggestions SET [Status] = 2 WHERE GoalId = '{goalId}'"));
    }

    [Fact]
    public async Task Upgrading_keeps_existing_ledger_rows_as_summary_and_the_previous_release_can_still_insert_without_the_column()
    {
        // The state of the previous release: the schema right before TrainingGoals.
        var cs = await NewDatabaseAsync(targetMigration: "20261115210545_BodyBarRunType");
        var user = Guid.NewGuid();
        const string OldInsert = """
            INSERT INTO AiGenerations (Id, UserId, IsGuest, CreatedAt, Outcome, Attempts, InputTokens, OutputTokens, DurationMs, PromptVersion, ModelDeployment)
            VALUES (NEWID(), '{0}', 1, SYSUTCDATETIME(), 0, 1, 100, 50, 900, 1, 'test')
            """;
        await ExecuteAsync(cs, string.Format(System.Globalization.CultureInfo.InvariantCulture, OldInsert, user));

        await using (var db = CreateContext(cs, AnonymousCurrentUser.Instance))
        {
            await db.Database.MigrateAsync(Ct);
        }

        // Rows of the old release written after the upgrade (a rolling deployment) also default to Summary.
        await ExecuteAsync(cs, string.Format(System.Globalization.CultureInfo.InvariantCulture, OldInsert, user));
        Assert.Equal(2, await ScalarAsync<int>(cs, $"SELECT COUNT(*) FROM AiGenerations WHERE UserId = '{user}' AND Feature = 0"));
        Assert.Equal(0, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM Goals"));
    }
}
