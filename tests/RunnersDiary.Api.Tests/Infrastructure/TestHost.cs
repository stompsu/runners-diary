using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace RunnersDiary.Api.Tests.Infrastructure;

/// <summary>Settings shared by both test factories.</summary>
internal static class TestHost
{
    /// <summary>Tests can pretend to be a client at any IP by sending this header (TestServer has no remote address).</summary>
    public const string ClientIpHeader = "X-Test-Client-Ip";

    public static void ConfigureAuth(IWebHostBuilder builder, bool entraEnabled)
    {
        builder.UseSetting("Auth:Guest:SigningKey", TestTokens.GuestSigningKey);
        // Mandatory outside Development (per-IP limits depend on it); tests read the client IP from ClientIpHeader instead.
        builder.UseSetting("ForwardedHeaders:Enabled", "false");
        // The general limiter must not interfere with functional tests; dedicated tests lower it.
        builder.UseSetting("RateLimiting:General:TokenLimit", "100000");
        builder.UseSetting("RateLimiting:General:TokensPerPeriod", "100000");

        if (!entraEnabled)
        {
            builder.UseSetting("Auth:Entra:Enabled", "false");
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ClientIpStartupFilter>());
            return;
        }

        builder.UseSetting("Auth:Entra:Enabled", "true");
        builder.UseSetting("Auth:Entra:TenantId", TestTokens.TenantId);
        builder.UseSetting("Auth:Entra:TenantSubdomain", TestTokens.TenantSubdomain);
        builder.UseSetting("Auth:Entra:Audience", TestTokens.ApiAudience);
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ClientIpStartupFilter>());
        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>("Entra", options =>
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(TestTokens.EntraConfiguration())));
    }

    /// <summary>Sets the connection's remote address from <see cref="ClientIpHeader"/>; runs before the application's own pipeline.</summary>
    private sealed class ClientIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(ClientIpHeader, out var value) && IPAddress.TryParse(value.ToString(), out var ip))
                {
                    context.Connection.RemoteIpAddress = ip;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
