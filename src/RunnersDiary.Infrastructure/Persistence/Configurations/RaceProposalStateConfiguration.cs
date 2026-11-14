using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Users;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

/// <summary>A user's mark on a catalogue race (ADR 0037 §3). Owner-filtered; removed with the user and with the race. "Signed up" is not stored (it is derived from <c>Runs.CatalogRaceId</c>).</summary>
internal sealed class RaceProposalStateConfiguration : IEntityTypeConfiguration<RaceProposalState>
{
    public void Configure(EntityTypeBuilder<RaceProposalState> b)
    {
        b.ToTable("RaceProposalStates", t => t.HasCheckConstraint("CK_RaceProposalStates_State", "[State] IN (1, 2)"));

        // Per-user reads are a seek on UserId plus an id list: cluster on (UserId, RaceId).
        b.HasKey(s => new { s.UserId, s.RaceId });
        b.HasIndex(s => s.RaceId).HasDatabaseName("IX_RaceProposalStates_RaceId");

        b.Property(s => s.State).HasColumnType("tinyint");
        b.Property(s => s.RaceDateWhenMarked).HasColumnType("date");

        b.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Race>().WithMany().HasForeignKey(s => s.RaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
