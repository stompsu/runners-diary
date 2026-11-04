using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Domain.Ai;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

/// <summary>The usage ledger (ADR 0028 §4): no foreign key to users on purpose, so a guest's usage outlives the guest for the global budgets.</summary>
internal sealed class AiGenerationConfiguration : IEntityTypeConfiguration<AiGeneration>
{
    public void Configure(EntityTypeBuilder<AiGeneration> b)
    {
        b.ToTable("AiGenerations");

        // Per-user quota reads are "this user's rows by time": cluster on (UserId, CreatedAt); the GUID primary key is non-clustered.
        b.HasKey(g => g.Id).IsClustered(false);
        b.HasIndex(g => new { g.UserId, g.CreatedAt }).IsClustered().HasDatabaseName("CX_AiGenerations_User_Created");
        // Global quotas and the 30-day purge scan by time only.
        b.HasIndex(g => g.CreatedAt).IncludeProperties(g => g.IsGuest).HasDatabaseName("IX_AiGenerations_Created");

        b.Property(g => g.Id).ValueGeneratedNever();
        b.Property(g => g.Outcome).HasColumnType("tinyint");
        b.Property(g => g.Attempts).HasColumnType("tinyint");
        b.Property(g => g.PromptVersion).HasColumnType("smallint");
        b.Property(g => g.ModelDeployment).HasMaxLength(64).IsUnicode(false).IsRequired();
    }
}
