using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Api.Tests.Insights;

/// <summary>The EF ledger store with the M9 feature column (ADR 0040 §6) on real SQL Server: per-feature user rows, weighted global sums, the covering index.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class AiLedgerFeatureStoreTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rows_keep_their_feature_and_user_reads_are_per_feature()
    {
        factory.SkipIfDockerUnavailable();
        _ = factory.CreateClient();
        var user = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await factory.WithDbAsync(async db =>
        {
            var store = new EfAiGenerationStore(db);
            await store.AddAsync(new AiGenerationRecord(user, false, now.AddMinutes(-3), AiGenerationOutcome.Succeeded, 1, 10, 10, 10, 2, "gpt-test"), Ct);
            await store.AddAsync(new AiGenerationRecord(user, false, now.AddMinutes(-2), AiGenerationOutcome.InvalidOutput, 2, 10, 10, 10, 1, "gpt-test", AiFeature.Plan), Ct);
            await store.AddAsync(new AiGenerationRecord(user, false, now.AddMinutes(-1), AiGenerationOutcome.Throttled, 1, 10, 10, 10, 1, "gpt-test", AiFeature.Plan), Ct);

            Assert.Single(await store.GetUserGenerationTimesAsync(user, AiFeature.Summary, null, Ct));
            Assert.Single(await store.GetUserGenerationTimesAsync(user, AiFeature.Plan, now.AddHours(-1), Ct));
            return 0;
        });

        var features = await factory.WithDbAsync(db => db.AiGenerations.AsNoTracking().Where(g => g.UserId == user).OrderBy(g => g.CreatedAt).Select(g => g.Feature).ToListAsync(Ct));
        Assert.Equal([AiFeature.Summary, AiFeature.Plan, AiFeature.Plan], features);
    }

    [Fact]
    public async Task The_global_sums_weight_plan_attempts()
    {
        factory.SkipIfDockerUnavailable();
        _ = factory.CreateClient();
        var since = DateTimeOffset.UtcNow.AddHours(-24);

        var (beforeAll, beforeGuests, beforeAll5, beforeGuests5) = await factory.WithDbAsync(async db =>
        {
            var store = new EfAiGenerationStore(db);
            var w3 = await store.GetGlobalUsageAsync(since, 3, Ct);
            var w5 = await store.GetGlobalUsageAsync(since, 5, Ct);
            return (w3.Attempts, w3.GuestAttempts, w5.Attempts, w5.GuestAttempts);
        });

        // The database is shared by the collection (tests run one at a time), so the assertions use the delta of the rows written here (a guest's
        // 2 plan attempts, a user's summary): weight 3 adds 2 x 3 + 1 = 7 (guests 6), weight 5 adds 11 (guests 10).
        var now = DateTimeOffset.UtcNow;
        var (afterAll, afterGuests, afterAll5, afterGuests5) = await factory.WithDbAsync(async db =>
        {
            var store = new EfAiGenerationStore(db);
            await store.AddAsync(new AiGenerationRecord(Guid.NewGuid(), true, now, AiGenerationOutcome.Succeeded, 2, 10, 10, 10, 1, "gpt-test", AiFeature.Plan), Ct);
            await store.AddAsync(new AiGenerationRecord(Guid.NewGuid(), false, now, AiGenerationOutcome.Succeeded, 1, 10, 10, 10, 2, "gpt-test"), Ct);
            var w3 = await store.GetGlobalUsageAsync(since, 3, Ct);
            var w5 = await store.GetGlobalUsageAsync(since, 5, Ct);
            return (w3.Attempts, w3.GuestAttempts, w5.Attempts, w5.GuestAttempts);
        });

        Assert.Equal((7, 6, 11, 10), (afterAll - beforeAll, afterGuests - beforeGuests, afterAll5 - beforeAll5, afterGuests5 - beforeGuests5));
        // Every plan attempt in the window adds (5 - 3) more with weight 5 than with weight 3.
        var planAttempts = await factory.WithDbAsync(db => db.AiGenerations.Where(g => g.CreatedAt > since.UtcDateTime && g.Feature == AiFeature.Plan).SumAsync(g => (int)g.Attempts, Ct));
        Assert.Equal(2 * planAttempts, afterAll5 - afterAll);
    }

    [Fact]
    public async Task The_time_index_covers_the_global_sum()
    {
        factory.SkipIfDockerUnavailable();
        _ = factory.CreateClient();

        var included = await factory.WithDbAsync(db => db.Database.SqlQueryRaw<string>(
            """
            SELECT c.name AS Value FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.name = 'IX_AiGenerations_Created' AND ic.is_included_column = 1
            """).ToListAsync(Ct));

        Assert.Equal(["Attempts", "Feature", "IsGuest"], included.Order(StringComparer.Ordinal));
    }
}
