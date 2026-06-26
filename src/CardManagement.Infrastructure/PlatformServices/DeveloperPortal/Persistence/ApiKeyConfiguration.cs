using CardManagement.Domain.PlatformServices.DeveloperPortal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.DeveloperPortal.Persistence;

/// <summary>
/// EF Core entity configuration for ApiKey.
/// Stores only the SHA-256 hash of the raw key — the raw key is never persisted.
/// </summary>
public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("DeveloperApiKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Id)
            .ValueGeneratedNever();

        builder.Property(k => k.DeveloperId)
            .IsRequired();

        builder.HasIndex(k => k.DeveloperId)
            .HasDatabaseName("IX_DeveloperApiKeys_DeveloperId");

        builder.Property(k => k.KeyHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(k => k.KeyHash)
            .IsUnique()
            .HasDatabaseName("IX_DeveloperApiKeys_KeyHash");

        builder.Property(k => k.KeyPrefix)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(k => k.Scopes)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(k => k.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(k => k.Status)
            .HasDatabaseName("IX_DeveloperApiKeys_Status");

        builder.Property(k => k.IsSandbox)
            .IsRequired();

        builder.Property(k => k.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(k => k.ExpiresAtUtc)
            .IsRequired(false)
            .HasPrecision(3);

        builder.Property(k => k.GracePeriodEndsAtUtc)
            .IsRequired(false)
            .HasPrecision(3);

        // Composite index for counting active keys per developer
        builder.HasIndex(k => new { k.DeveloperId, k.Status })
            .HasDatabaseName("IX_DeveloperApiKeys_DeveloperId_Status");
    }
}
