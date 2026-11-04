using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Maps;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Tests.Maps;

/// <summary>The shared daily geocode budget on GET /api/geocoding/search (ADR 0033 section 2). No Docker, no network.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GeocodeBudgetEndpointTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri Search(string query = "?q=Gliwice") => new("/api/geocoding/search" + query, UriKind.Relative);

    private WebApplicationFactory<Program> WithFake(int limit) =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Maps:Geocoding:GlobalDailyLimit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.ConfigureServices(services => services.AddSingleton<IGeocoder, FakeGeocoder>());
        });

    // The budget is durable and global, so tests sharing the database start from an empty table.
    private static async Task ResetBudgetAsync(WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();
        await db.GeocodeUsages.ExecuteDeleteAsync(Ct);
    }

    [Fact]
    public async Task Calls_over_the_budget_are_a_429_rate_limited_problem_with_the_budget_code_and_a_retry_after_until_midnight()
    {
        factory.SkipIfDockerUnavailable();
        using var host = WithFake(2);
        await ResetBudgetAsync(host);
        var one = host.EntraUser("budget-one");
        var two = host.EntraUser("budget-two");

        Assert.Equal(HttpStatusCode.OK, (await one.Client.GetAsync(Search(), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await two.Client.GetAsync(Search(), Ct)).StatusCode); // another caller spends the same budget
        var spent = await one.Client.GetAsync(Search(), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, spent.StatusCode);
        Assert.Equal("application/problem+json", spent.Content.Headers.ContentType?.MediaType);
        var problem = await spent.ReadAsync();
        Assert.Equal("urn:runnersdiary:problem:rate-limited", problem.GetProperty("type").GetString());
        Assert.Equal("geocode-budget", problem.GetProperty("code").GetString());
        var secondsToMidnight = spent.Headers.RetryAfter?.Delta?.TotalSeconds ?? 0;
        Assert.InRange(secondsToMidnight, 1, 86400);
    }

    [Fact]
    public async Task Invalid_requests_do_not_spend_the_budget()
    {
        factory.SkipIfDockerUnavailable();
        using var host = WithFake(1);
        await ResetBudgetAsync(host);
        var user = host.EntraUser("budget-invalid");

        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.GetAsync(Search("?q=a"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.GetAsync(Search("?q=Gliwice&lat=91&lon=1"), Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync(Search(), Ct)).StatusCode);
    }

    [Fact]
    public async Task Without_a_provider_the_budget_is_untouched_and_the_answer_stays_503()
    {
        factory.SkipIfDockerUnavailable();
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Maps:Geocoding:GlobalDailyLimit", "1");
        });
        var user = host.EntraUser("budget-none");

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await user.Client.GetAsync(Search(), Ct)).StatusCode);
        }
    }
}
