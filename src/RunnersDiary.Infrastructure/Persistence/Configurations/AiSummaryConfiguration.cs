using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Domain.Users;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

internal sealed class AiSummaryConfiguration : IEntityTypeConfiguration<AiSummary>
{
    public void Configure(EntityTypeBuilder<AiSummary> b)
    {
        b.ToTable("AiSummaries");

        // Every read is "this user's summary for one key": the unique natural key is the clustered index; the GUID primary key is not.
        b.HasKey(s => s.Id).IsClustered(false);
        b.HasIndex(s => new { s.UserId, s.PeriodFrom, s.PeriodTo, s.Compare, s.Language })
            .IsUnique()
            .IsClustered()
            .HasDatabaseName("UX_AiSummaries_Key");

        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.PeriodFrom).HasColumnType("date");
        b.Property(s => s.PeriodTo).HasColumnType("date");
        b.Property(s => s.Compare).HasColumnType("tinyint");
        b.Property(s => s.Language).HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(s => s.MetricsVersion).HasColumnType("smallint");
        b.Property(s => s.FactSheetVersion).HasColumnType("smallint");
        b.Property(s => s.PromptVersion).HasColumnType("smallint");
        b.Property(s => s.ModelDeployment).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(s => s.ModelId).HasMaxLength(Ai.EfAiSummaryStore.MaxModelIdLength).IsUnicode(false);
        b.Property(s => s.Fingerprint).HasColumnType("binary(32)").IsRequired();
        b.Property(s => s.ContentJson).HasColumnType("nvarchar(max)").IsRequired();

        // Deleting a user (guest cleanup) removes the summaries with it.
        b.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
