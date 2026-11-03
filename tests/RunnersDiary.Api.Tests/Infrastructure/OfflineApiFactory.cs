using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace RunnersDiary.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the API with connection strings pointing at endpoints that refuse connections.
/// Needs no Docker; used for tests that don't depend on healthy infrastructure.
/// </summary>
public sealed class OfflineApiFactory : WebApplicationFactory<Program>
{
    public const string ThrowPath = "/__test/throw";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Production: problem details must not include exception details.
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:runnersdiary",
            "Server=127.0.0.1,1;Database=runnersdiary;Integrated Security=false;User Id=test;Password=unused;Connect Timeout=2;TrustServerCertificate=true");
        builder.UseSetting("ConnectionStrings:blobs",
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;");
        // Fail fast instead of retrying against the unreachable endpoints.
        builder.UseSetting("Aspire:Microsoft:EntityFrameworkCore:SqlServer:DisableRetry", "true");
        builder.UseSetting("Aspire:Azure:Storage:Blobs:ClientOptions:Retry:MaxRetries", "0");
        // Entra disabled: this factory proves the anonymous/401 behaviour and never needs a database for authentication.
        TestHost.ConfigureAuth(builder, entraEnabled: false);

        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ThrowingEndpointStartupFilter>());
    }

    /// <summary>
    /// A test-only anonymous endpoint that throws, so the app's exception handler (and nothing else) is under test. The endpoint is set
    /// before routing, i.e. it exists outside the app's endpoint table and cannot affect the endpoint inventory tests.
    /// </summary>
    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        private static readonly Endpoint ThrowEndpoint = new(
            _ => throw new InvalidOperationException("Test exception with secret detail"),
            new EndpointMetadataCollection(new AllowAnonymousAttribute()),
            "test-throw");

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Path == ThrowPath)
                {
                    context.SetEndpoint(ThrowEndpoint);
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
