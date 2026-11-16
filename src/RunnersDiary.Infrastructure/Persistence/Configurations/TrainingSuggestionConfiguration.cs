using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

/// <summary>
/// Suggested sessions of a goal's plan (ADR 0041 §1): Owner-filtered, no FK to <c>Users</c> on purpose: the single cascade path is
/// <c>Users -> Goals -> TrainingSuggestions</c> through the composite FK <c>(GoalId, UserId)</c>, the <c>GpxTracks</c> pattern.
/// </summary>
internal sealed class TrainingSuggestionConfiguration : IEntityTypeConfiguration<TrainingSuggestion>
{
    public void Configure(EntityTypeBuilder<TrainingSuggestion> b)
    {
        b.ToTable("TrainingSuggestions", t =>
        {
            t.HasCheckConstraint("CK_TrainingSuggestions_Status", "[Status] IN (0, 1)");
            t.HasCheckConstraint("CK_TrainingSuggestions_Pace",
                "([TargetPaceMinSecondsPerKm] IS NULL AND [TargetPaceMaxSecondsPerKm] IS NULL)"
                + " OR ([TargetPaceMinSecondsPerKm] IS NOT NULL AND [TargetPaceMaxSecondsPerKm] IS NOT NULL AND [TargetPaceMinSecondsPerKm] <= [TargetPaceMaxSecondsPerKm])");
        });

        // The calendar range read is "this user's suggestions in a date range": cluster on (UserId, Date); the GUID primary key is non-clustered.
        b.HasKey(s => s.Id).IsClustered(false);
        b.HasIndex(s => new { s.UserId, s.Date }).IsClustered().HasDatabaseName("CX_TrainingSuggestions_User_Date");
        // One session per day per goal.
        b.HasIndex(s => new { s.GoalId, s.Date }).IsUnique().HasDatabaseName("UX_TrainingSuggestions_Goal_Date");

        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Date).HasColumnType("date");
        b.Property(s => s.DistanceMeters).HasColumnType("float");
        b.Property(s => s.DurationSeconds).HasColumnType("int");
        b.Property(s => s.Intensity).HasColumnType("tinyint");
        b.Property(s => s.TargetPaceMinSecondsPerKm).HasColumnType("smallint").HasConversion<short>();
        b.Property(s => s.TargetPaceMaxSecondsPerKm).HasColumnType("smallint").HasConversion<short>();
        b.Property(s => s.Description).HasMaxLength(300).IsRequired();
        b.Property(s => s.Phase).HasColumnType("tinyint");
        b.Property(s => s.Status).HasColumnType("tinyint");

        b.HasOne<Goal>()
            .WithMany()
            .HasForeignKey(s => new { s.GoalId, s.UserId })
            .HasPrincipalKey(g => new { g.Id, g.UserId })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne<RunType>().WithMany().HasForeignKey(s => s.RunTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
