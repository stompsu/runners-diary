using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Domain.Races;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

/// <summary>
/// The shared race catalogue (ADR 0036 §1): no user id and deliberately <b>no Owner filter</b> (documented exception, ADR 0010 §6).
/// Written only by the import job and the Development/Testing demo seeder; the API only reads it.
/// </summary>
internal sealed class RaceConfiguration : IEntityTypeConfiguration<Race>
{
    public void Configure(EntityTypeBuilder<Race> b)
    {
        // Every proposal query is a date window: cluster on Date; the GUID primary key is non-clustered.
        b.ToTable("Races", t =>
        {
            t.HasCheckConstraint("CK_Races_Price", "([PriceAmount] IS NULL AND [PriceCurrency] IS NULL) OR ([PriceAmount] IS NOT NULL AND [PriceCurrency] IS NOT NULL)");
            t.HasCheckConstraint("CK_Races_Duplicate", "[DuplicateOfRaceId] IS NULL OR [DuplicateOfRaceId] <> [Id]");
        });

        b.HasKey(r => r.Id).IsClustered(false);
        b.HasIndex(r => r.Date).IsClustered().HasDatabaseName("CX_Races_Date");
        b.HasIndex(r => new { r.Source, r.ExternalId }).IsUnique().HasDatabaseName("UX_Races_Source_External");
        b.HasIndex(r => r.DuplicateOfRaceId).HasFilter("[DuplicateOfRaceId] IS NOT NULL").HasDatabaseName("IX_Races_DuplicateOf");

        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Source).HasColumnType("tinyint");
        b.Property(r => r.ExternalId).HasMaxLength(RaceFacts.ExternalIdMaxLength).IsUnicode(false).IsRequired();
        b.Property(r => r.Name).HasMaxLength(RaceFacts.NameMaxLength).IsRequired();
        b.Property(r => r.Date).HasColumnType("date");
        b.Property(r => r.StartTime).HasColumnType("time(0)");
        b.Property(r => r.TimeZoneId).HasMaxLength(64).IsUnicode(false);
        b.Property(r => r.LocationName).HasMaxLength(RaceFacts.LocationNameMaxLength);
        b.Property(r => r.Point).HasConversion<GeoPointConverter>().HasColumnType("geography");
        b.Property(r => r.CountryCode).HasMaxLength(2).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(r => r.RegionCode).HasMaxLength(6).IsUnicode(false);
        b.Property(r => r.Kind).HasColumnType("tinyint");
        b.Property(r => r.Surface).HasColumnType("tinyint");
        b.Property(r => r.ElevationGainMeters).HasColumnType("float");
        b.Property(r => r.SignupUrl).HasMaxLength(RaceFacts.UrlMaxLength).IsUnicode(false);
        b.Property(r => r.EventUrl).HasMaxLength(RaceFacts.UrlMaxLength).IsUnicode(false);
        b.Property(r => r.SourceUrl).HasMaxLength(RaceFacts.UrlMaxLength).IsUnicode(false);
        b.Property(r => r.PriceAmount).HasColumnType("decimal(9,2)");
        b.Property(r => r.PriceCurrency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        b.Property(r => r.SignupDeadline).HasColumnType("date");
        b.Property(r => r.OrganizerName).HasMaxLength(RaceFacts.OrganizerNameMaxLength);
        b.Property(r => r.FactsHash).HasColumnType("binary(32)").IsRequired();
        b.Property(r => r.PreviousDate).HasColumnType("date");
        b.Property(r => r.MissedScans).HasColumnType("tinyint");
        b.Property(r => r.DetailETag).HasMaxLength(200).IsUnicode(false);
        b.Property(r => r.DetailLastModified).HasMaxLength(64).IsUnicode(false);

        b.Ignore(r => r.DistancesMeters);
        b.Ignore(r => r.IsRemoved);
        b.HasMany(r => r.Distances).WithOne().HasForeignKey(d => d.RaceId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Distances).UsePropertyAccessMode(PropertyAccessMode.Field);

        // A duplicate points at its canonical row; NO ACTION, so the purge must never delete a canonical row that others still point to.
        b.HasOne<Race>().WithMany().HasForeignKey(r => r.DuplicateOfRaceId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class RaceDistanceConfiguration : IEntityTypeConfiguration<RaceDistance>
{
    public void Configure(EntityTypeBuilder<RaceDistance> b)
    {
        b.ToTable("RaceDistances");
        b.HasKey(d => new { d.RaceId, d.DistanceMeters });
        b.Property(d => d.DistanceMeters).HasColumnType("float").ValueGeneratedNever();
    }
}
