using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

internal sealed class RunTypeConfiguration : IEntityTypeConfiguration<RunType>
{
    public void Configure(EntityTypeBuilder<RunType> b)
    {
        b.ToTable("RunTypes");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.Code).HasMaxLength(40).IsUnicode(false).IsRequired();
        b.Property(t => t.Category).HasColumnType("tinyint");
        b.HasIndex(t => t.Code).IsUnique().HasDatabaseName("UX_RunTypes_Code");

        b.HasData(RunTypeCatalog.All);
    }
}
