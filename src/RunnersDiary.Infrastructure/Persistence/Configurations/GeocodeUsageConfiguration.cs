using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

/// <summary>The durable daily geocode budget (ADR 0033 section 2): one row per UTC day.</summary>
internal sealed class GeocodeUsageConfiguration : IEntityTypeConfiguration<GeocodeUsage>
{
    public void Configure(EntityTypeBuilder<GeocodeUsage> b)
    {
        b.ToTable("GeocodeUsage");
        b.HasKey(g => g.Day);
        b.Property(g => g.Day).HasColumnType("date").ValueGeneratedNever();
    }
}
