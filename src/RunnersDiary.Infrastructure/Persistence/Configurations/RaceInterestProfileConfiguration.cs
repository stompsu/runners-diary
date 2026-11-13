using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Users;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

/// <summary>A user's race interests (ADR 0037 §1): one row per user, Owner-filtered, removed with the user.</summary>
internal sealed class RaceInterestProfileConfiguration : IEntityTypeConfiguration<RaceInterestProfile>
{
    public void Configure(EntityTypeBuilder<RaceInterestProfile> b)
    {
        b.ToTable("RaceInterestProfiles", t =>
        {
            t.HasCheckConstraint("CK_RaceInterestProfiles_Kinds", "[Kinds] BETWEEN 1 AND 127");
            t.HasCheckConstraint("CK_RaceInterestProfiles_Center",
                "([Center] IS NULL AND [RadiusMeters] IS NULL AND [CenterName] IS NULL) OR ([Center] IS NOT NULL AND [RadiusMeters] IS NOT NULL)");
            t.HasCheckConstraint("CK_RaceInterestProfiles_Radius", "[RadiusMeters] IS NULL OR [RadiusMeters] BETWEEN 5000 AND 300000");
            t.HasCheckConstraint("CK_RaceInterestProfiles_Distance",
                "([MinDistanceMeters] IS NULL OR [MinDistanceMeters] BETWEEN 1000 AND 250000)"
                + " AND ([MaxDistanceMeters] IS NULL OR [MaxDistanceMeters] BETWEEN 1000 AND 250000)"
                + " AND ([MinDistanceMeters] IS NULL OR [MaxDistanceMeters] IS NULL OR [MinDistanceMeters] <= [MaxDistanceMeters])");
            t.HasCheckConstraint("CK_RaceInterestProfiles_Horizon", "[HorizonDays] BETWEEN 7 AND 365");
        });

        b.HasKey(p => p.UserId);
        b.Property(p => p.UserId).ValueGeneratedNever();
        b.Property(p => p.Kinds).HasColumnType("tinyint");

        // JSON array text in an explicit varchar(200) column: 16 codes of at most 6 characters fit (ADR 0037 §1). Not the EF 10 json type.
        b.PrimitiveCollection(p => p.RegionCodes).HasField("_regionCodes").HasColumnType("varchar(200)").IsRequired();

        b.Property(p => p.Center).HasConversion<GeoPointConverter>().HasColumnType("geography");
        b.Property(p => p.CenterName).HasMaxLength(RaceInterestRules.CenterNameMaxLength);
        b.Property(p => p.RadiusMeters).HasColumnType("int");
        b.Property(p => p.MinDistanceMeters).HasColumnType("float");
        b.Property(p => p.MaxDistanceMeters).HasColumnType("float");
        b.Property(p => p.HorizonDays).HasColumnType("smallint").HasConversion<short>();
        b.Property(p => p.Keywords).HasMaxLength(RaceInterestRules.KeywordsMaxLength);

        b.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
