using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Users;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

internal sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> b)
    {
        b.ToTable("Runs");

        // Every hot query is "this user's runs in a date range": cluster on (UserId, Date); the GUID primary key is non-clustered.
        b.HasKey(r => r.Id).IsClustered(false);
        b.HasAlternateKey(r => new { r.Id, r.UserId }).HasName("AK_Runs_Id_UserId");
        b.HasIndex(r => new { r.UserId, r.Date }).IsClustered().HasDatabaseName("CX_Runs_User_Date");
        b.HasIndex(r => r.RunTypeId).HasDatabaseName("IX_Runs_RunTypeId");

        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Date).HasColumnType("date");
        b.Property(r => r.StartTime).HasColumnType("time(0)");
        b.Property(r => r.TimeZoneId).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(r => r.Status).HasColumnType("tinyint");
        b.Property(r => r.Surface).HasColumnType("tinyint");
        b.Property(r => r.Title).HasMaxLength(120);
        b.Property(r => r.Notes).HasMaxLength(4000);
        b.Property(r => r.DistanceMeters).HasColumnType("float");
        b.Property(r => r.DurationSeconds).HasColumnType("int");
        b.Property(r => r.ElevationGainMeters).HasColumnType("float");
        b.Property(r => r.ElevationLossMeters).HasColumnType("float");
        b.Property(r => r.AvgHeartRate).HasColumnType("smallint").HasConversion<short>();
        b.Property(r => r.MaxHeartRate).HasColumnType("smallint").HasConversion<short>();
        b.Property(r => r.Rpe).HasColumnType("tinyint").HasConversion<byte>();
        b.Property(r => r.Gear).HasMaxLength(100);
        b.Property(r => r.Weather).HasMaxLength(200);
        b.Property(r => r.RowVersion).IsRowVersion();

        b.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<RunType>().WithMany().HasForeignKey(r => r.RunTypeId).OnDelete(DeleteBehavior.Restrict);

        b.Navigation(r => r.Tracks).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Optional complex types share the Runs row (columns Location_*, Race_*, Rating_*).
        b.ComplexProperty(r => r.Location, c =>
        {
            c.Property(l => l.Point).HasConversion<GeoPointConverter>().HasColumnType("geography").IsRequired();
            c.Property(l => l.Name).HasMaxLength(200);
        });

        b.ComplexProperty(r => r.Race, c =>
        {
            c.Property(x => x.Name).HasMaxLength(150).IsRequired();
            c.Property(x => x.DistanceCategory).HasColumnType("tinyint");
            c.Property(x => x.OfficialDistanceMeters).HasColumnType("float");
            c.Property(x => x.BibNumber).HasMaxLength(20);
            c.Property(x => x.CategoryName).HasMaxLength(50);
            c.Property(x => x.ResultsUrl).HasMaxLength(2048).IsUnicode(false);
        });

        b.ComplexProperty(r => r.Rating, c =>
        {
            c.Property(x => x.Stars).HasColumnType("tinyint").HasConversion<byte>().IsRequired();
            c.Property(x => x.Comment).HasMaxLength(500);
        });
    }
}
