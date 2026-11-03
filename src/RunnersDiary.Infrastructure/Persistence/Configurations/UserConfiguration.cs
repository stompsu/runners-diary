using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RunnersDiary.Domain.Users;

namespace RunnersDiary.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users", t => t.HasCheckConstraint(
            "CK_Users_Kind",
            "([Kind] = 1 AND [ExpiresAt] IS NOT NULL AND [ExternalTenantId] IS NULL AND [ExternalObjectId] IS NULL) " +
            "OR ([Kind] = 0 AND [ExpiresAt] IS NULL AND [ExternalTenantId] IS NOT NULL AND [ExternalObjectId] IS NOT NULL)"));

        b.HasKey(u => u.Id);
        b.Property(u => u.Id).ValueGeneratedNever();
        b.Property(u => u.Kind).HasColumnType("tinyint");
        b.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength);
        b.Property(u => u.TimeZoneId).HasMaxLength(64).IsUnicode(false);
        b.Property(u => u.UnitSystem).HasColumnType("tinyint");

        b.HasIndex(u => new { u.ExternalTenantId, u.ExternalObjectId })
            .IsUnique()
            .HasFilter("[ExternalObjectId] IS NOT NULL")
            .HasDatabaseName("UX_Users_External");
        b.HasIndex(u => u.ExpiresAt)
            .HasFilter("[Kind] = 1")
            .HasDatabaseName("IX_Users_Guest_ExpiresAt");
    }
}
