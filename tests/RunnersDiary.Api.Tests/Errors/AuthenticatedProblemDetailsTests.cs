using System.Net;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Errors;

[Collection(ContainersFixtureGroup.Name)]
public sealed class AuthenticatedProblemDetailsTests(ContainersApiFactory factory)
{
    [Fact]
    public async Task UnknownApiRoute_WithToken_Returns404ProblemDetails()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("lost");

        var response = await user.Client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, (await response.ReadAsync()).GetProperty("status").GetInt32());
    }
}
