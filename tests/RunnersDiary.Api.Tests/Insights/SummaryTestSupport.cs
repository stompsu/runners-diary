using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Tests.Insights;

/// <summary>Shared pieces of the AI summary API tests: a fixed two-week period with enough sessions to need a real summary, and DB probes.</summary>
internal static class SummaryTestSupport
{
    public const string From = "2025-03-03";
    public const string To = "2025-03-16";
    public const string Query = "?from=" + From + "&to=" + To;

    public static Uri Url(string query = Query) => new("/api/insights/summary" + query, UriKind.Relative);

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Four completed runs inside <see cref="From"/>..<see cref="To"/>: above the "few sessions" template threshold. Returns id and version of each.</summary>
    public static async Task<List<(Guid Id, string Version)>> SeedRunsAsync(TestActor actor, Action<Dictionary<string, object?>>? change = null)
    {
        var created = new List<(Guid, string)>();
        foreach (var date in new[] { "2025-03-04", "2025-03-06", "2025-03-09", "2025-03-12" })
        {
            var response = await actor.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run(date, change), Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.ReadAsync();
            created.Add((body.GetProperty("id").GetGuid(), body.GetProperty("version").GetString()!));
        }

        return created;
    }

    public static Task<HttpResponseMessage> PostAsync(TestActor actor, object? body = null, CancellationToken? cancellationToken = null) =>
        actor.Client.PostAsJsonAsync(new Uri("/api/insights/summary", UriKind.Relative), body ?? new { from = From, to = To }, cancellationToken ?? Ct);

    public static async Task<JsonElement> GetOkAsync(TestActor actor, string query = Query)
    {
        var response = await actor.Client.GetAsync(Url(query), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync();
    }

    public static async Task<JsonElement> PostOkAsync(TestActor actor, object? body = null)
    {
        var response = await PostAsync(actor, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync();
    }

    public static Task<List<AiGeneration>> LedgerAsync(this WebApplicationFactory<Program> factory, Guid userId) =>
        factory.WithDbAsync(db => db.AiGenerations.AsNoTracking().Where(g => g.UserId == userId).OrderBy(g => g.CreatedAt).ToListAsync(Ct));

    public static Task<List<AiSummary>> SummariesAsync(this WebApplicationFactory<Program> factory, Guid userId) =>
        factory.WithDbAsync(db => db.AiSummaries.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).AsNoTracking()
            .Where(s => s.UserId == userId).ToListAsync(Ct));

    /// <summary>Inserts ledger rows directly, e.g. aged ones, since the ledger has no public write path besides a generation.</summary>
    public static async Task SeedLedgerAsync(this WebApplicationFactory<Program> factory, Guid userId, bool isGuest, DateTime createdAtUtc, int count = 1)
    {
        await factory.WithDbAsync(async db =>
        {
            for (var i = 0; i < count; i++)
            {
                db.AiGenerations.Add(AiGeneration.Create(userId, isGuest, createdAtUtc, AiGenerationOutcome.Succeeded, 1, 10, 10, 10, 1, "gpt-test"));
            }

            await db.SaveChangesAsync(Ct);
            return 0;
        });
    }

    /// <summary>The global budgets' view of the last 24 h: model attempts (sum of <c>Attempts</c>) of everybody and of guests.</summary>
    public static async Task<(int Total, int Guests)> GlobalLedgerAsync(this WebApplicationFactory<Program> factory)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        return await factory.WithDbAsync(async db => (
            await db.AiGenerations.Where(g => g.CreatedAt > since).SumAsync(g => (int)g.Attempts, Ct),
            await db.AiGenerations.Where(g => g.CreatedAt > since && g.IsGuest).SumAsync(g => (int)g.Attempts, Ct)));
    }

    public static string ProblemType(this JsonElement problem) => problem.GetProperty("type").GetString()!;
}
