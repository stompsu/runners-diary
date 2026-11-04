using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Pipeline;

/// <summary>ADR 0028 §4 quotas computed from the ledger (plan AC8, the Domain side; step 3 adds the SQL-backed store tests).</summary>
public sealed class AiUsageLedgerTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryAiGenerationStore _store = new();
    private readonly AiOptions _options = new();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private AiUsageLedger Ledger => new(_store, Options.Create(_options), _clock);

    private Task Add(Guid user, bool guest, TimeSpan ago, AiGenerationOutcome outcome = AiGenerationOutcome.Succeeded, int attempts = 1) =>
        _store.AddAsync(new AiGenerationRecord(user, guest, _clock.GetUtcNow() - ago, outcome, attempts, 100, 10, 1000, 1, "gpt-test"), CancellationToken.None);

    private Task<QuotaStatus> Status(Guid user, bool guest) => Ledger.GetStatusAsync(user, guest, TestContext.Current.CancellationToken);

    [Fact]
    public async Task User_quota_is_a_rolling_24_hours_with_reset_time()
    {
        _options.Quotas.UserPer24h = 3;
        await Add(User, false, TimeSpan.FromHours(25));                 // outside the window
        await Add(User, false, TimeSpan.FromHours(20));
        await Add(User, false, TimeSpan.FromHours(5), AiGenerationOutcome.InvalidOutput);
        await Add(Other, false, TimeSpan.FromHours(1));

        var open = await Status(User, false);
        Assert.Equal((2, 3, QuotaWindow.Rolling24h, QuotaBlock.None, 1), (open.Used, open.Limit, open.Window, open.Block, open.Remaining));

        await Add(User, false, TimeSpan.FromHours(1), AiGenerationOutcome.Timeout);
        var full = await Status(User, false);
        Assert.Equal(QuotaBlock.Account, full.Block);
        Assert.Equal(3, full.BlockLimit);
        Assert.Equal(_clock.GetUtcNow() + TimeSpan.FromHours(4), full.ResetsAt);

        _clock.Advance(TimeSpan.FromHours(4));
        Assert.Equal(QuotaBlock.None, (await Status(User, false)).Block);
    }

    [Fact]
    public async Task Reset_time_accounts_for_an_overshoot()
    {
        _options.Quotas.UserPer24h = 2;
        await Add(User, false, TimeSpan.FromHours(10));
        await Add(User, false, TimeSpan.FromHours(8));
        await Add(User, false, TimeSpan.FromHours(6));  // a race let a third one through

        var status = await Status(User, false);
        Assert.Equal(3, status.Used);
        Assert.Equal(_clock.GetUtcNow() + TimeSpan.FromHours(16), status.ResetsAt);
    }

    [Fact]
    public async Task Guest_quota_is_per_account_without_reset()
    {
        _options.Quotas.GuestPerAccount = 3;
        await Add(User, true, TimeSpan.FromHours(23));
        await Add(User, true, TimeSpan.FromHours(30));
        await Add(User, true, TimeSpan.FromMinutes(5), AiGenerationOutcome.ProviderError);

        var status = await Status(User, true);
        Assert.Equal((3, 3, QuotaWindow.Account, QuotaBlock.Account), (status.Used, status.Limit, status.Window, status.Block));
        Assert.Null(status.ResetsAt);
        Assert.Equal(QuotaBlock.None, (await Status(Other, true)).Block);
    }

    [Fact]
    public async Task Global_and_guest_global_quotas()
    {
        _options.Quotas.GlobalPer24h = 4;
        _options.Quotas.GuestGlobalPer24h = 2;
        await Add(Guid.NewGuid(), true, TimeSpan.FromHours(1));
        await Add(Guid.NewGuid(), true, TimeSpan.FromHours(2));
        await Add(Guid.NewGuid(), true, TimeSpan.FromHours(30));  // outside the window

        var guest = await Status(User, true);
        Assert.Equal((QuotaBlock.Global, 2), (guest.Block, guest.BlockLimit!.Value));
        Assert.Equal(QuotaBlock.None, (await Status(User, false)).Block);

        await Add(Guid.NewGuid(), false, TimeSpan.FromHours(1));
        await Add(Guid.NewGuid(), false, TimeSpan.FromHours(1));
        var user = await Status(User, false);
        Assert.Equal((QuotaBlock.Global, 4), (user.Block, user.BlockLimit!.Value));
        Assert.Null(user.ResetsAt);
    }

    /// <summary>M2: the global budgets count model attempts (a two-attempt generation counts twice); account quotas count generations.</summary>
    [Fact]
    public async Task Global_quotas_count_attempts_and_account_quotas_count_generations()
    {
        _options.Quotas.GlobalPer24h = 4;
        _options.Quotas.GuestGlobalPer24h = 3;
        _options.Quotas.UserPer24h = 3;
        await Add(User, false, TimeSpan.FromHours(1), AiGenerationOutcome.InvalidOutput, attempts: 2);
        await Add(Guid.NewGuid(), true, TimeSpan.FromHours(2), AiGenerationOutcome.Succeeded, attempts: 2);

        var user = await Status(User, false);
        Assert.Equal(1, user.Used);                         // one generation for the account...
        Assert.Equal(QuotaBlock.Global, user.Block);        // ...but two rows of two attempts reach the global limit of 4
        Assert.Equal(4, user.BlockLimit);

        _options.Quotas.GlobalPer24h = 5;
        Assert.Equal(QuotaBlock.None, (await Status(User, false)).Block);
    }

    [Fact]
    public async Task Guest_global_quota_counts_guest_attempts()
    {
        _options.Quotas.GlobalPer24h = 100;
        _options.Quotas.GuestGlobalPer24h = 3;
        await Add(Guid.NewGuid(), true, TimeSpan.FromHours(1), AiGenerationOutcome.Succeeded, attempts: 2);
        Assert.Equal(QuotaBlock.None, (await Status(User, true)).Block);

        await Add(Guid.NewGuid(), true, TimeSpan.FromHours(1), AiGenerationOutcome.Timeout, attempts: 1);
        Assert.Equal((QuotaBlock.Global, 3), ((await Status(User, true)).Block, (await Status(User, true)).BlockLimit!.Value));
        Assert.Equal(QuotaBlock.None, (await Status(User, false)).Block);
    }

    /// <summary>M4: a Throttled row (provider 429 after a billed attempt) never counts toward the caller's quota, only the global attempts.</summary>
    [Fact]
    public async Task Throttled_rows_count_globally_but_not_for_the_account()
    {
        _options.Quotas.UserPer24h = 1;
        _options.Quotas.GuestPerAccount = 1;
        _options.Quotas.GlobalPer24h = 2;
        await Add(User, false, TimeSpan.FromHours(1), AiGenerationOutcome.Throttled, attempts: 1);
        await Add(Other, true, TimeSpan.FromHours(1), AiGenerationOutcome.Throttled, attempts: 1);

        var user = await Status(User, false);
        Assert.Equal(0, user.Used);
        Assert.Equal(QuotaBlock.Global, user.Block);
        Assert.Equal(0, (await Status(Other, true)).Used);

        _options.Quotas.GlobalPer24h = 3;
        Assert.Equal(QuotaBlock.None, (await Status(User, false)).Block);
    }

    [Fact]
    public async Task An_exhausted_account_reports_account_before_global()
    {
        _options.Quotas.UserPer24h = 1;
        _options.Quotas.GlobalPer24h = 1;
        await Add(User, false, TimeSpan.FromHours(1));
        Assert.Equal(QuotaBlock.Account, (await Status(User, false)).Block);
    }

    [Fact]
    public async Task Purge_removes_only_rows_older_than_thirty_days()
    {
        await Add(User, false, TimeSpan.FromDays(31));
        await Add(User, true, TimeSpan.FromDays(30) + TimeSpan.FromMinutes(1));
        await Add(User, false, TimeSpan.FromDays(29));

        Assert.Equal(2, await Ledger.PurgeAsync(TestContext.Current.CancellationToken));
        Assert.Single(_store.Rows);
    }
}
