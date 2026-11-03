using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RunnersDiary.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations before the host starts serving, only when <c>Database:MigrateOnStartup=true</c> (set by the AppHost).
/// Never enabled in Azure: production schema changes go through a reviewed script (ADR 0013 §7). EF takes a migration lock, so concurrent starts are safe.
/// </summary>
internal sealed class DatabaseMigrator(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
