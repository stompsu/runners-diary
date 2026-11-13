using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Api.Tests.Races;

/// <summary>Helpers of the race endpoint tests: unique catalogue races (found through a unique keyword) and interests that match only them.</summary>
internal static class RaceTestSupport
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A keyword unique to one test (2-30 letters/digits, lowercase) so the shared catalogue of other tests never matches.</summary>
    public static string NewToken() => "zq" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Today in UTC; tests place races at least ten days ahead, so a caller's zone never shifts them into the past or beyond the horizon.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static RaceFacts Facts(
        string token,
        int daysAhead,
        string suffix = "Run",
        RaceKind kind = RaceKind.Road,
        double[]? distances = null,
        string region = "PL-24",
        GeoPoint? point = null) => new(
        RaceSource.Demo,
        $"{token}-{suffix}-{Guid.NewGuid():N}",
        $"{token} {suffix}",
        Today.AddDays(daysAhead),
        new TimeOnly(9, 0),
        "Gliwice",
        point ?? new GeoPoint(50.2945, 18.6714),
        "PL",
        region,
        kind,
        kind == RaceKind.Trail ? Surface.Trail : Surface.Road,
        distances ?? [10_000],
        120,
        "https://example.org/signup",
        "https://example.org/event",
        "https://example.org/source",
        49.99m,
        "PLN",
        Today.AddDays(daysAhead - 3),
        "Test Club");

    public static async Task<Race> SeedAsync(this WebApplicationFactory<Program> factory, RaceFacts facts, Action<Race>? change = null)
    {
        var race = Race.Create(facts, DateTimeOffset.UtcNow);
        change?.Invoke(race);
        await factory.WithDbAsync(async db =>
        {
            db.Races.Add(race);
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        return race;
    }

    /// <summary>Upserts the synthetic demo catalogue (idempotent), as the Development/Testing startup seeder does.</summary>
    public static async Task EnsureDemoCatalogueAsync(this WebApplicationFactory<Program> factory) =>
        await factory.Services.GetRequiredService<DemoRaceCatalogue>().SeedAsync(Ct);

    public static async Task MutateRaceAsync(this WebApplicationFactory<Program> factory, Guid raceId, Action<Race> change) =>
        await factory.WithDbAsync(async db =>
        {
            var race = await db.Races.Include(r => r.Distances).SingleAsync(r => r.Id == raceId, Ct);
            change(race);
            await db.SaveChangesAsync(Ct);
            return 0;
        });

    public static object Interests(string? keywords, Action<Dictionary<string, object?>>? change = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["kinds"] = new[] { "Road", "Trail", "Mountain", "Ultra", "CrossCountry", "Ocr", "Other" },
            ["regionCodes"] = Array.Empty<string>(),
            ["center"] = null,
            ["radiusMeters"] = null,
            ["minDistanceMeters"] = null,
            ["maxDistanceMeters"] = null,
            ["horizonDays"] = 365,
            ["keywords"] = keywords,
            ["showInCalendar"] = true,
        };
        change?.Invoke(body);
        return body;
    }

    public static async Task<HttpResponseMessage> PutInterestsAsync(this TestActor actor, object body) =>
        await actor.Client.PutAsJsonAsync(new Uri("/api/races/interests", UriKind.Relative), body, Ct);

    public static async Task ConfigureAsync(this TestActor actor, string? keywords, Action<Dictionary<string, object?>>? change = null)
    {
        var response = await actor.PutInterestsAsync(Interests(keywords, change));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    public static async Task<JsonElement> ProposalsAsync(this TestActor actor, int fromDays = 0, int toDays = 90, bool includeDismissed = false)
    {
        var from = Today.AddDays(fromDays).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var to = Today.AddDays(toDays).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var response = await actor.Client.GetAsync(new Uri($"/api/races/proposals?from={from}&to={to}&includeDismissed={includeDismissed.ToString().ToLowerInvariant()}", UriKind.Relative), Ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync();
    }

    public static async Task<HttpResponseMessage> SetStateAsync(this TestActor actor, Guid raceId, string state) =>
        await actor.Client.PutAsJsonAsync(new Uri($"/api/races/{raceId}/state", UriKind.Relative), new { state }, Ct);

    public static async Task<HttpResponseMessage> SignUpAsync(this TestActor actor, Guid raceId, object? body = null) =>
        await actor.Client.PostAsJsonAsync(new Uri($"/api/races/{raceId}/signup", UriKind.Relative), body ?? new { }, Ct);

    public static async Task<HttpResponseMessage> GetRaceAsync(this TestActor actor, Guid raceId) =>
        await actor.Client.GetAsync(new Uri($"/api/races/{raceId}", UriKind.Relative), Ct);

    public static JsonElement ItemFor(this JsonElement list, Guid raceId) =>
        list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("raceId").GetGuid() == raceId);

    public static bool Contains(this JsonElement list, Guid raceId) =>
        list.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("raceId").GetGuid() == raceId);

    public static async Task<int> StateRowsAsync(this WebApplicationFactory<Program> factory, Guid userId) =>
        await factory.WithDbAsync(db => db.RaceProposalStates.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).CountAsync(s => s.UserId == userId, Ct));
}
