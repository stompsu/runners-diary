using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Infrastructure.Ai;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using static RunnersDiary.Api.Tests.Insights.SummaryTestSupport;

namespace RunnersDiary.Api.Tests.Insights;

/// <summary>AC7 and cleanup: freshness tuple, stored-row caps, guest cleanup and the 30-day ledger purge.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class InsightsSummaryCacheTests(ContainersApiFactory factory)
{
    [Fact]
    public async Task A_run_edit_makes_the_summary_stale_but_it_keeps_its_stored_values_and_a_title_edit_does_not()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid();
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("cstale");
        var runs = await SeedRunsAsync(user);
        var created = await PostOkAsync(user);
        var before = created.GetProperty("summary").GetProperty("values").ToString();

        var title = await user.Client.PutAsJsonAsync(new Uri($"/api/runs/{runs[0].Id}", UriKind.Relative),
            Json.Run("2025-03-04", r => { r["title"] = "Renamed"; r["version"] = runs[0].Version; }), Ct);
        Assert.Equal(HttpStatusCode.OK, title.StatusCode);
        Assert.Equal("fresh", (await GetOkAsync(user)).GetProperty("state").GetString()); // free text is not part of the facts

        var edit = await user.Client.PutAsJsonAsync(new Uri($"/api/runs/{runs[1].Id}", UriKind.Relative),
            Json.Run("2025-03-06", r => { r["distanceMeters"] = 21_000; r["version"] = runs[1].Version; }), Ct);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var stale = await GetOkAsync(user);
        Assert.Equal("stale", stale.GetProperty("state").GetString());
        Assert.Equal("model", stale.GetProperty("summary").GetProperty("source").GetString());
        Assert.Equal(before, stale.GetProperty("summary").GetProperty("values").ToString());
        // A POST without regenerate does not reuse a stale row.
        client.ReplyValid();
        var again = await PostOkAsync(user);
        Assert.Equal("fresh", again.GetProperty("state").GetString());
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task A_different_deployment_or_prompt_version_makes_a_stored_summary_stale()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid();
        using var host = AiTestHost.Create(factory, client);
        using var other = AiTestHost.Create(factory, new ScriptedChatClient(), deployment: "gpt-other");
        var user = host.EntraUser("cdeploy");
        await SeedRunsAsync(user);
        var userId = await user.GetUserIdAsync();
        await PostOkAsync(user);
        var otherView = other.CreateClient();
        otherView.DefaultRequestHeaders.Authorization = user.Client.DefaultRequestHeaders.Authorization;

        var viaOther = await otherView.GetAsync(Url(), Ct);
        Assert.Equal("stale", (await viaOther.ReadAsync()).GetProperty("state").GetString());

        Assert.Equal("fresh", (await GetOkAsync(user)).GetProperty("state").GetString());
        await host.WithDbAsync(db => db.AiSummaries.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PromptVersion, (short)99), Ct));
        Assert.Equal("stale", (await GetOkAsync(user)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_partial_period_goes_stale_when_today_changes()
    {
        factory.SkipIfDockerUnavailable();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = new ScriptedChatClient().ReplyValid();
        using var host = AiTestHost.Create(factory, client, services: s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(clock);
        });
        var user = host.EntraUser("cpartial");
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        foreach (var offset in new[] { -9, -7, -5, -3 })
        {
            var date = today.AddDays(offset).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(HttpStatusCode.Created, (await user.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run(date), Ct)).StatusCode);
        }

        var from = today.AddDays(-10).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var to = today.AddDays(10).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var body = new { from, to };
        Assert.Equal("fresh", (await PostOkAsync(user, body)).GetProperty("state").GetString());
        Assert.Equal("fresh", (await GetOkAsync(user, $"?from={from}&to={to}")).GetProperty("state").GetString());

        clock.Advance(TimeSpan.FromDays(1));

        Assert.Equal("stale", (await GetOkAsync(user, $"?from={from}&to={to}")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_guest_keeps_at_most_20_summaries_and_the_oldest_are_dropped_and_other_users_are_untouched()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("ccap");
        var neighbour = await factory.GuestAsync("ccapn");
        var options = factory.Services.GetRequiredService<DbContextOptions<RunnersDiaryDbContext>>();
        var created = DateTimeOffset.UtcNow.AddHours(-1);

        async Task UpsertAsync(Guid id, int day)
        {
            await using var db = new RunnersDiaryDbContext(options, new FixedCurrentUser(id, isGuest: true));
            var store = new EfAiSummaryStore(db, new FixedCurrentUser(id, isGuest: true));
            var from = new DateOnly(2025, 1, 1).AddDays(day);
            await store.UpsertAsync(new StoredSummary(
                new SummaryKey(from, from.AddDays(6), ComparisonKind.Previous, "en"), 1, 1, 1, "gpt-test", null, new byte[32],
                "{}", 1, 1, created.AddSeconds(day)), Ct);
        }

        await UpsertAsync(neighbour.UserId!.Value, 0);
        for (var day = 0; day < 25; day++)
        {
            await UpsertAsync(guest.UserId!.Value, day);
        }

        var rows = await factory.SummariesAsync(guest.UserId!.Value);
        Assert.Equal(20, rows.Count);
        Assert.Equal(new DateOnly(2025, 1, 6), rows.Min(r => r.PeriodFrom)); // days 0..4 were dropped
        Assert.Single(await factory.SummariesAsync(neighbour.UserId!.Value));

        await UpsertAsync(guest.UserId!.Value, 24); // replacing an existing key does not drop anything
        Assert.Equal(20, (await factory.SummariesAsync(guest.UserId!.Value)).Count);
    }

    [Fact]
    public async Task Guest_cleanup_removes_summaries_and_purges_only_ledger_rows_older_than_30_days()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid();
        using var host = AiTestHost.Create(factory, client);
        var guest = await host.GuestAsync("cclean");
        await SeedRunsAsync(guest);
        await PostOkAsync(guest);
        var id = guest.UserId!.Value;
        Assert.Single(await host.SummariesAsync(id));
        await host.SeedLedgerAsync(id, true, DateTime.UtcNow.AddDays(-31));
        await host.SeedLedgerAsync(id, true, DateTime.UtcNow.AddDays(-29));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await factory.SetGuestExpiryAsync(id, clock.GetUtcNow().UtcDateTime.AddHours(-2));
        var cleanup = new GuestCleanup(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Services.GetRequiredService<RunnersDiary.Infrastructure.Storage.IBlobStore>(),
            clock, Options.Create(new GuestOptions { Cleanup = { BatchSize = 1000 } }), NullLogger<GuestCleanup>.Instance);

        await cleanup.RunOnceAsync(Ct);

        Assert.False(await factory.UserExistsAsync(id));
        Assert.Empty(await factory.SummariesAsync(id));
        var ledger = await factory.LedgerAsync(id); // the ledger outlives the guest (global budgets), minus rows past retention
        Assert.Equal(2, ledger.Count);
        Assert.DoesNotContain(ledger, g => g.CreatedAt < DateTime.UtcNow.AddDays(-30));
    }
}
