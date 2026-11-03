using System.Net;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Api.Tests.RunTypes;

[Collection(ContainersFixtureGroup.Name)]
public sealed class RunTypesEndpointTests(ContainersApiFactory factory)
{
    [Fact]
    public async Task Lists_the_catalog_in_sort_order()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("rt");

        var response = await user.Client.GetAsync(new Uri("/api/run-types", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.ReadAsync()).EnumerateArray().ToList();
        Assert.Equal(RunTypeCatalog.All.Count, items.Count);
        Assert.Equal(RunTypeCatalog.All.OrderBy(t => t.SortOrder).Select(t => t.Id), items.Select(i => (short)i.GetProperty("id").GetInt32()));
        var rest = items.Single(i => i.GetProperty("code").GetString() == "rest");
        Assert.True(rest.GetProperty("isRestDay").GetBoolean());
        Assert.Equal("Other", rest.GetProperty("category").GetString());
        Assert.Equal("Race", items.First(i => i.GetProperty("code").GetString() == "roadRace").GetProperty("category").GetString());
    }
}
