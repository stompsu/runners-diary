using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RunnersDiary.Api.Http;

namespace RunnersDiary.Api.Tests.Readiness;

public sealed class ProductionConfigurationValidatorTests
{
    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "RunnersDiary.Api";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => KeyValuePair.Create(v.Key, v.Value))).Build();

    private static readonly (string, string?)[] Connections =
    [
        ("ConnectionStrings:runnersdiary", "Server=tcp:x,1433"),
        ("ConnectionStrings:blobs", "https://x.blob.core.windows.net/"),
    ];

    private static IConfiguration WithConnections(params (string Key, string? Value)[] more) => Config([.. Connections, .. more]);

    [Fact]
    public void A_complete_production_configuration_passes()
    {
        ProductionConfigurationValidator.ValidateApi(
            WithConnections(
                ("ForwardedHeaders:Enabled", "true"),
                ("ForwardedHeaders:ForwardLimit", "2"),
                ("ForwardedHeaders:KnownIPNetworks:0", "0.0.0.0/0"),
                ("ForwardedHeaders:KnownIPNetworks:1", "::/0")),
            new Environment("Production"));
    }

    [Theory]
    [InlineData("ConnectionStrings:runnersdiary")]
    [InlineData("ConnectionStrings:blobs")]
    public void A_missing_connection_string_fails_naming_the_key(string missing)
    {
        var config = Config(Connections.Where(c => c.Item1 != missing).ToArray());

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.ValidateApi(config, new Environment("Production")));
        Assert.Contains(missing, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Failures_are_reported_together_and_never_echo_values()
    {
        var config = Config(
            ("ConnectionStrings:blobs", "secret-value-123"),
            ("ForwardedHeaders:Enabled", "true"),
            ("ForwardedHeaders:ForwardLimit", "0"),
            ("ForwardedHeaders:KnownIPNetworks:0", "not-a-cidr-999"));

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.ValidateApi(config, new Environment("Production")));

        Assert.Contains("ConnectionStrings:runnersdiary", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ForwardedHeaders:ForwardLimit", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ForwardedHeaders:KnownIPNetworks", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value-123", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-cidr-999", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_forwarded_headers_need_no_networks()
    {
        ProductionConfigurationValidator.ValidateApi(WithConnections(("ForwardedHeaders:Enabled", "false")), new Environment("Production"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Other_environments_are_not_validated(string environment)
    {
        ProductionConfigurationValidator.ValidateApi(Config(), new Environment(environment));
        ProductionConfigurationValidator.ValidateJob(Config(), new Environment(environment));
    }

    [Fact]
    public void The_job_host_needs_only_the_connection_strings()
    {
        ProductionConfigurationValidator.ValidateJob(WithConnections(), new Environment("Production"));
        Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.ValidateJob(Config(), new Environment("Production")));
    }

    [Fact]
    public void Seeding_the_demo_race_catalogue_is_rejected_in_production_for_the_api_and_the_job()
    {
        var config = WithConnections(("Races:DemoCatalogue:SeedOnStartup", "true"));

        var api = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.ValidateApi(config, new Environment("Production")));
        Assert.Contains("Races:DemoCatalogue:SeedOnStartup", api.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.ValidateJob(config, new Environment("Production")));

        ProductionConfigurationValidator.ValidateJob(WithConnections(("Races:DemoCatalogue:SeedOnStartup", "false")), new Environment("Production"));
        ProductionConfigurationValidator.ValidateApi(config, new Environment("Development"));
    }
}
