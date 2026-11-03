using Microsoft.Extensions.Hosting;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Connection string names; they match the Aspire resource names in the AppHost.</summary>
    public const string DatabaseConnectionName = "runnersdiary";
    public const string BlobsConnectionName = "blobs";

    /// <summary>
    /// Registers the SQL DbContext and the Blob client from Aspire-provided connection strings.
    /// Both integrations add health checks, retries and telemetry.
    /// </summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddSqlServerDbContext<RunnersDiaryDbContext>(DatabaseConnectionName);
        builder.AddAzureBlobServiceClient(BlobsConnectionName);

        return builder;
    }
}
