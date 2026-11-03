using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Auth;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Security;

/// <summary>Startup rules from ADR 0009 §4-5: fail closed outside Development, degrade cleanly inside it.</summary>
public sealed class AuthStartupTests(OfflineApiFactory factory) : IClassFixture<OfflineApiFactory>
{
    [Fact]
    public void Missing_guest_signing_key_outside_Development_fails_startup()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Auth:Guest:SigningKey", ""));

        var ex = Assert.Throws<InvalidOperationException>(() => host.CreateClient());
        Assert.Contains("SigningKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Too_short_guest_signing_key_fails_startup()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Auth:Guest:SigningKey", "short"));

        Assert.Throws<InvalidOperationException>(() => host.CreateClient());
    }

    [Fact]
    public void Unconfigured_Entra_outside_Development_fails_startup_unless_explicitly_disabled()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Auth:Entra:Enabled", ""));

        var ex = Assert.Throws<InvalidOperationException>(() => host.CreateClient());
        Assert.Contains("Auth:Entra", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Partially_configured_Entra_fails_startup()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:Entra:Enabled", "");
            b.UseSetting("Auth:Entra:TenantId", TestTokens.TenantId);
        });

        Assert.Throws<InvalidOperationException>(() => host.CreateClient());
    }

    [Fact]
    public void Development_without_a_signing_key_and_without_Entra_starts_with_an_ephemeral_key()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Auth:Guest:SigningKey", "");
            b.UseSetting("Auth:Entra:Enabled", "");
        });

        _ = host.CreateClient();

        var issuer = host.Services.GetRequiredService<GuestTokenIssuer>();
        Assert.False(string.IsNullOrEmpty(issuer.Issue(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1))));
    }
}
