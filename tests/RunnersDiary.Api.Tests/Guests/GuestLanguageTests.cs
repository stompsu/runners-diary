using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Time;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Goals;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Guests;

/// <summary>M10 AC3/AC7 (ADR 0042 §3, §7): <c>POST /api/guest-sessions?lang=</c> and the language-aware demo seeding.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GuestLanguageTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri Endpoint(string query) => new("/api/guest-sessions" + query, UriKind.Relative);

    [Theory]
    [InlineData("?lang=pl", "pl")]
    [InlineData("?lang=en", "en")]
    [InlineData("", null)]
    public async Task The_language_of_the_guest_follows_the_lang_query(string query, string? expected)
    {
        factory.SkipIfDockerUnavailable();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsync(Endpoint(query), content: null, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.ReadAsync()).GetProperty("userId").GetGuid();
        var stored = await factory.WithDbAsync(db => db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Language).SingleAsync(Ct));
        Assert.Equal(expected, stored);
    }

    [Theory]
    [InlineData("?lang=de")]
    [InlineData("?lang=PL")]
    [InlineData("?lang=")]
    [InlineData("?lang=pl-PL")]
    public async Task An_unsupported_lang_is_a_400_and_creates_no_guest(string query)
    {
        factory.SkipIfDockerUnavailable();
        using var anonymous = factory.CreateClient();
        var before = await factory.ActiveGuestCountAsync();

        var response = await anonymous.PostAsync(Endpoint(query), content: null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadAsync()).GetProperty("errors").TryGetProperty("lang", out _));
        Assert.Equal(before, await factory.ActiveGuestCountAsync());
    }

    [Fact]
    public async Task An_unsupported_lang_is_never_echoed()
    {
        factory.SkipIfDockerUnavailable();
        using var anonymous = factory.CreateClient();
        const string marker = "zz<x>\"'echo";

        var response = await anonymous.PostAsync(Endpoint("?lang=" + Uri.EscapeDataString(marker)), content: null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("echo", body, StringComparison.Ordinal);
        Assert.DoesNotContain("zz", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_polish_guest_is_seeded_with_the_mapped_texts_and_unmapped_ones_stay_english()
    {
        factory.SkipIfDockerUnavailable();
        var directory = Directory.CreateTempSubdirectory("rd-pl-seed").FullName;
        try
        {
            var seedRoot = Path.Combine(AppContext.BaseDirectory, "seed", "demo");
            File.Copy(Path.Combine(seedRoot, "training-plan.json"), Path.Combine(directory, "training-plan.json"));
            var options = Options.Create(new GuestOptions { DemoDataPath = directory });
            var localizer = new DemoTextLocalizer(options);
            var plan = new DemoTrainingPlan(options, localizer);
            var loader = factory.Services.GetRequiredService<DemoDatasetLoader>();

            // Test fixture map: every required text except the goal title (to see the fallback).
            var required = DemoTextLocalizer.RequiredTexts(loader.Get().Template, plan.Template).Where(t => t != plan.Template.Goal.Title);
            Directory.CreateDirectory(Path.Combine(directory, DemoTextLocalizer.DirectoryName));
            var texts = required.ToDictionary(t => t, t => "PL " + t);
            await File.WriteAllTextAsync(
                Path.Combine(directory, DemoTextLocalizer.DirectoryName, "pl.json"),
                System.Text.Json.JsonSerializer.Serialize(new { version = 1, texts }), Ct);

            var seeder = new DemoDataSeeder(
                loader, plan, localizer, factory.Services.GetRequiredService<IBlobStore>(), factory.Services.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System, NullLogger<DemoDataSeeder>.Instance);

            var guest = User.CreateGuest(DateTimeOffset.UtcNow, language: "pl");
            await seeder.SeedAsync(guest, Ct);

            var notes = await factory.WithDbAsync(db => db.Runs.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter])
                .Where(r => r.UserId == guest.Id && r.Notes != null).Select(r => r.Notes!).ToListAsync(Ct));
            Assert.NotEmpty(notes);
            Assert.All(notes, n => Assert.StartsWith("PL ", n, StringComparison.Ordinal));

            var goal = await factory.WithDbAsync(db => db.Goals.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).SingleAsync(g => g.UserId == guest.Id, Ct));
            Assert.Equal(plan.Template.Goal.Title, goal.Title); // no entry: English
            Assert.StartsWith("PL ", goal.PlanNotes, StringComparison.Ordinal);
            Assert.Equal("pl", goal.PlanLanguage);
            var descriptions = await factory.WithDbAsync(db => db.TrainingSuggestions.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter])
                .Where(s => s.UserId == guest.Id).Select(s => s.Description).ToListAsync(Ct));
            Assert.NotEmpty(descriptions);
            Assert.All(descriptions, d => Assert.StartsWith("PL ", d, StringComparison.Ordinal));

            // An English guest through the same seeder is untouched.
            var english = User.CreateGuest(DateTimeOffset.UtcNow, language: "en");
            await seeder.SeedAsync(english, Ct);
            var englishGoal = await factory.WithDbAsync(db => db.Goals.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).SingleAsync(g => g.UserId == english.Id, Ct));
            Assert.Equal("en", englishGoal.PlanLanguage);
            Assert.DoesNotContain("PL ", englishGoal.PlanNotes, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_guest_endpoint_seeds_a_polish_guest_with_the_committed_polish_demo_texts()
    {
        factory.SkipIfDockerUnavailable();
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsync(Endpoint("?lang=pl"), content: null, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var client = factory.CreateClient().WithBearer((await response.ReadAsync()).GetProperty("accessToken").GetString()!);

        var me = await (await client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).ReadAsync();
        Assert.Equal("pl", me.GetProperty("language").GetString());
        var today = UserToday.Get(TimeProvider.System, "Europe/Warsaw");
        var month = await (await client.GetAsync(new Uri($"/api/calendar/months/{today.Year}/{today.Month}", UriKind.Relative), Ct)).ReadAsync();
        Assert.NotEmpty(month.GetProperty("runs").EnumerateArray());

        // End to end with the committed pl.json: no demo note is left in English.
        var id = me.GetProperty("id").GetGuid();
        var english = await factory.WithDbAsync(db => db.Runs.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter])
            .Where(r => r.UserId == id && r.Notes != null).Select(r => r.Notes!).ToListAsync(Ct));
        Assert.NotEmpty(english);
        Assert.DoesNotContain(english, n => n is "Rest day." or "Travel day." or "Shake-out.");
        Assert.Contains(english, n => n == "Dzień odpoczynku.");
        var goal = await factory.WithDbAsync(db => db.Goals.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).SingleAsync(g => g.UserId == id, Ct));
        Assert.Equal("pl", goal.PlanLanguage);
        Assert.Equal("Demo: trail w Beskidach 25K", goal.Title);
    }
}
