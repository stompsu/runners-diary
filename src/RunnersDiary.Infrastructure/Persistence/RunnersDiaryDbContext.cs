using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;

namespace RunnersDiary.Infrastructure.Persistence;

/// <summary>
/// Application database context (non-pooled: it takes the scoped <see cref="ICurrentUser"/>).
/// User-owned aggregates carry the named query filter "Owner" (ADR 0010); only guest cleanup and the demo seeder may bypass it.
/// </summary>
public sealed class RunnersDiaryDbContext(DbContextOptions<RunnersDiaryDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public const string OwnerFilter = "Owner";

    public DbSet<User> Users => Set<User>();

    public DbSet<RunType> RunTypes => Set<RunType>();

    public DbSet<Run> Runs => Set<Run>();

    public DbSet<GpxTrack> GpxTracks => Set<GpxTrack>();

    /// <summary>Read by the query filter on every query (EF parameterizes it per context instance) and by the ownership guard.</summary>
    internal Guid? CurrentUserId => currentUser.UserIdOrNull;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RunnersDiaryDbContext).Assembly);

        modelBuilder.Entity<Run>().HasQueryFilter(OwnerFilter, r => r.UserId == CurrentUserId);
        modelBuilder.Entity<GpxTrack>().HasQueryFilter(OwnerFilter, t => t.UserId == CurrentUserId);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2(0)").HaveConversion<UtcDateTimeConverter>();
    }
}
