using Microsoft.AspNetCore.Builder;
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

        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ThrowingEndpointStartupFilter>());
    }

    /// <summary>Appends a test-only path that throws, after the app's own pipeline (so its exception handler applies).</summary>
    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((context, nextMiddleware) =>
                context.Request.Path == ThrowPath
                    ? throw new InvalidOperationException("Test exception with secret detail")
                    : nextMiddleware(context));
        };
    }
}
