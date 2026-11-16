using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Users;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

/// <summary>
/// Training goals (ADR 0039): per user, Owner-filtered, removed with the user. The link to a planned race run is a composite NO ACTION FK to
/// <c>Runs(Id, UserId)</c> (also enforcing "same owner"); the application clears it when the run is deleted (a second cascade path from
/// <c>Users</c> is not allowed in SQL Server).
/// </summary>
internal sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> b)
    {
        b.ToTable("Goals", t =>
        {
            t.HasCheckConstraint("CK_Goals_Type", "[Type] IN (0, 1, 2)");
            t.HasCheckConstraint("CK_Goals_Status", "[Status] IN (0, 1, 2)");
            t.HasCheckConstraint("CK_Goals_Distance", "[DistanceMeters] BETWEEN 1000 AND 250000");
            t.HasCheckConstraint("CK_Goals_Elevation", "[ElevationGainMeters] IS NULL OR [ElevationGainMeters] BETWEEN 0 AND 10000");
            t.HasCheckConstraint("CK_Goals_Target",
                "([Type] <> 2 OR [TargetDurationSeconds] IS NULL) AND ([Type] <> 1 OR [TargetDurationSeconds] IS NOT NULL)");
        });

        // Hot reads are "this user's goals": cluster on (UserId, TargetDate); the GUID primary key is non-clustered.
        b.HasKey(g => g.Id).IsClustered(false);
        // The principal key of the two composite FKs (TrainingSuggestions and Runs.GoalId).
        b.HasAlternateKey(g => new { g.Id, g.UserId }).HasName("AK_Goals_Id_UserId");
        b.HasIndex(g => new { g.UserId, g.TargetDate }).IsClustered().HasDatabaseName("CX_Goals_User_TargetDate");
        b.HasIndex(g => new { g.RaceRunId, g.UserId }).HasFilter("[RaceRunId] IS NOT NULL").HasDatabaseName("IX_Goals_RaceRunId_UserId");

        b.Property(g => g.Id).ValueGeneratedNever();
        b.Property(g => g.Type).HasColumnType("tinyint");
        b.Property(g => g.Title).HasMaxLength(120).IsRequired();
        b.Property(g => g.TargetDate).HasColumnType("date");
        b.Property(g => g.DistanceMeters).HasColumnType("float");
        b.Property(g => g.TargetDurationSeconds).HasColumnType("int");
        b.Property(g => g.ElevationGainMeters).HasColumnType("float");
        b.Property(g => g.Surface).HasColumnType("tinyint");
        b.Property(g => g.EventName).HasMaxLength(150);
        b.Property(g => g.Notes).HasMaxLength(1000);
        b.Property(g => g.Status).HasColumnType("tinyint");
        b.Property(g => g.PlanNotes).HasMaxLength(600);
        b.Property(g => g.PlanPromptVersion).HasColumnType("smallint");
        b.Property(g => g.PlanModelDeployment).HasMaxLength(64).IsUnicode(false);
        b.Property(g => g.PlanModelId).HasMaxLength(100).IsUnicode(false);
        b.Property(g => g.PlanHorizonEnd).HasColumnType("date");
        b.Property(g => g.RowVersion).IsRowVersion();

        b.HasOne<User>().WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Run>()
            .WithMany()
            .HasForeignKey(g => new { g.RaceRunId, g.UserId })
            .HasPrincipalKey(r => new { r.Id, r.UserId })
            .OnDelete(DeleteBehavior.NoAction);

        // The optional location shares the Goals row (columns Location_Point, Location_Name), like Runs.
        b.ComplexProperty(g => g.Location, c =>
        {
            c.Property(l => l.Point).HasConversion<GeoPointConverter>().HasColumnType("geography").IsRequired();
            c.Property(l => l.Name).HasMaxLength(200);
        });
    }
}
