using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Tracks;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

internal sealed class GpxTrackConfiguration : IEntityTypeConfiguration<GpxTrack>
{
    public void Configure(EntityTypeBuilder<GpxTrack> b)
    {
        b.ToTable("GpxTracks");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.Kind).HasColumnType("tinyint");
        b.Property(t => t.Status).HasColumnType("tinyint");
        b.Property(t => t.OriginalFileName).HasMaxLength(255).IsRequired();
        b.Property(t => t.SizeBytes).HasColumnType("bigint");
        b.Property(t => t.Sha256).HasColumnType("binary(32)").IsRequired();
        b.Property(t => t.RawBlobName).HasMaxLength(400).IsUnicode(false).IsRequired();

        // Composite FK to the run's alternate key (Id, UserId): a track can only belong to the same user as its run, enforced by the
        // database. UserId still has no FK to Users (Users -> Runs -> GpxTracks stays the only cascade path).
        b.HasOne<Run>()
            .WithMany(r => r.Tracks)
            .HasForeignKey(t => new { t.RunId, t.UserId })
            .HasPrincipalKey(r => new { r.Id, r.UserId })
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(t => new { t.RunId, t.Kind }).IsUnique().HasDatabaseName("UX_GpxTracks_Run_Kind");
        b.HasIndex(t => t.UserId).HasDatabaseName("IX_GpxTracks_UserId");
    }
}
