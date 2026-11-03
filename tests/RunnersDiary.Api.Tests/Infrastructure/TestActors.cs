using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Tests.Infrastructure;

/// <summary>A caller of the API in a test: an HTTP client with a bearer token, and the internal user id once known.</summary>
internal sealed class TestActor(string name, HttpClient client, Guid? userId = null)
{
    public string Name { get; } = name;

    public HttpClient Client { get; } = client;

    /// <summary>The internal <c>Users.Id</c> (known for guests; for Entra users after the first call, see <see cref="GetUserIdAsync"/>).</summary>
    public Guid? UserId { get; private set; } = userId;

    public async Task<Guid> GetUserIdAsync()
    {
        if (UserId is { } id)
        {
            return id;
        }

        var me = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/me", UriKind.Relative), TestContext.Current.CancellationToken);
        UserId = me.GetProperty("id").GetGuid();
        return UserId.Value;
    }

    public override string ToString() => Name;
}

internal static class TestActors
{
    public static TestActor EntraUser(this WebApplicationFactory<Program> factory, string name, Guid? objectId = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Entra(objectId ?? Guid.NewGuid(), name: name));
        return new TestActor(name, client);
    }

    /// <summary>
    /// A guest created through the real <c>POST /api/guest-sessions</c> endpoint: seeded demo diary, blobs and a server-issued token.
    /// </summary>
    public static async Task<TestActor> GuestSessionAsync(this WebApplicationFactory<Program> factory, string name)
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsync(new Uri("/api/guest-sessions", UriKind.Relative), content: null, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var body = await response.ReadAsync();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return new TestActor(name, client, body.GetProperty("userId").GetGuid());
    }

    /// <summary>
    /// A guest row created directly in the database (no demo data, no blobs) with a token minted by the test key; lets tests
    /// control the creation time and keeps them independent of the seeder.
    /// </summary>
    public static async Task<TestActor> GuestAsync(this WebApplicationFactory<Program> factory, string name, DateTimeOffset? createdAt = null)
    {
        var guest = User.CreateGuest(createdAt ?? DateTimeOffset.UtcNow);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();
            db.Users.Add(guest);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Guest(guest.Id));
        return new TestActor(name, client, guest.Id);
    }

    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

internal static class Json
{
    public static async Task<JsonElement> ReadAsync(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();

    /// <summary>A minimal valid completed easy run (type 20) on the given date; override via the dictionary.</summary>
    public static Dictionary<string, object?> Run(string date, Action<Dictionary<string, object?>>? change = null)
    {
        var run = new Dictionary<string, object?>
        {
            ["date"] = date,
            ["startTime"] = "07:30",
            ["timeZoneId"] = "Europe/Warsaw",
            ["status"] = "Completed",
            ["runTypeId"] = 20,
            ["title"] = "Morning run",
            ["distanceMeters"] = 10000,
            ["durationSeconds"] = 3000,
            ["rpe"] = 5,
        };
        change?.Invoke(run);
        return run;
    }
}
