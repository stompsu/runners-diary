using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Maps;

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

    public DbSet<AiSummary> AiSummaries => Set<AiSummary>();

    /// <summary>
    /// The AI usage ledger: a system table without the Owner filter (ADR 0028 §4). Only <c>EfAiGenerationStore</c> reads or writes it
    /// (the per-request ledger and the guest cleanup's 30-day purge both go through it); every per-user read there filters by an explicit user id.
    /// </summary>
    public DbSet<AiGeneration> AiGenerations => Set<AiGeneration>();

    /// <summary>The durable daily geocode budget: a system table without the Owner filter, only used by <see cref="GeocodeBudget"/> (and the cleanup's purge through it). Holds no user data.</summary>
    public DbSet<GeocodeUsage> GeocodeUsages => Set<GeocodeUsage>();

    /// <summary>
    /// The shared race catalogue: reference data without a user id and without the Owner filter (documented exception, ADR 0010 §6 / 0036 §1).
    /// The API only reads it; writes come from the import job and the Development/Testing demo seeder.
    /// </summary>
    public DbSet<Race> Races => Set<Race>();

    public DbSet<RaceInterestProfile> RaceInterestProfiles => Set<RaceInterestProfile>();

    public DbSet<RaceProposalState> RaceProposalStates => Set<RaceProposalState>();

    /// <summary>Read by the query filter on every query (EF parameterizes it per context instance) and by the ownership guard.</summary>
    internal Guid? CurrentUserId => currentUser.UserIdOrNull;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RunnersDiaryDbContext).Assembly);

        modelBuilder.Entity<Run>().HasQueryFilter(OwnerFilter, r => r.UserId == CurrentUserId);
        modelBuilder.Entity<GpxTrack>().HasQueryFilter(OwnerFilter, t => t.UserId == CurrentUserId);
        modelBuilder.Entity<AiSummary>().HasQueryFilter(OwnerFilter, s => s.UserId == CurrentUserId);
        modelBuilder.Entity<RaceInterestProfile>().HasQueryFilter(OwnerFilter, p => p.UserId == CurrentUserId);
        modelBuilder.Entity<RaceProposalState>().HasQueryFilter(OwnerFilter, s => s.UserId == CurrentUserId);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2(0)").HaveConversion<UtcDateTimeConverter>();
    }
}
