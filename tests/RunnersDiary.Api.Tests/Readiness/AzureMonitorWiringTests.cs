using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace RunnersDiary.Api.Tests.Readiness;

/// <summary>ADR 0034: the Azure Monitor exporter exists only when Azure provides the Application Insights connection string.</summary>
public sealed class AzureMonitorWiringTests
{
    private const string ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost.invalid/";

    private static HostApplicationBuilder BuilderWith(string? connectionString)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        // An in-memory value (even an empty one) wins over a developer machine's real environment variable.
        builder.Configuration[Extensions.ApplicationInsightsConnectionStringKey] = connectionString ?? string.Empty;
        builder.ConfigureOpenTelemetry();
        return builder;
    }

    private static bool HasAzureMonitorRegistrations(IServiceCollection services)
    {
        var assembly = typeof(AzureMonitorOptions).Assembly;
        return services.Any(d =>
            d.ServiceType.Assembly == assembly
            || d.ImplementationType?.Assembly == assembly
            || d.ServiceType.GenericTypeArguments.Any(a => a.Assembly == assembly));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_connection_string_no_azure_monitor_services_are_registered(string? value)
    {
        var builder = BuilderWith(value);

        Assert.False(HasAzureMonitorRegistrations(builder.Services));
    }

    [Fact]
    public void With_a_connection_string_the_exporter_is_registered_with_five_traces_per_second()
    {
        var builder = BuilderWith(ConnectionString);

        Assert.True(HasAzureMonitorRegistrations(builder.Services));
        using var host = builder.Build();
        Assert.Equal(5, host.Services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value.TracesPerSecond);
    }
}
