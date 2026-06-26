using CardManagement.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for IdempotencyRecordEntity.
/// Maps to PostgreSQL with JSONB response payload and TTL-based expiry.
/// </summary>
public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecordEntity>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecordEntity> builder)
    {
        builder.ToTable("IdempotencyRecords");

        builder.HasKey(r => r.Key);

        builder.Property(r => r.Key)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(r => r.Key)
            .IsUnique();

        builder.Property(r => r.RequestId)
            .IsRequired();

        builder.Property(r => r.ResponsePayload)
            .HasColumnType("jsonb");

        builder.Property(r => r.IsCompleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(r => r.ExpiresAtUtc)
            .IsRequired()
            .HasPrecision(3);

        // Index on ExpiresAtUtc for efficient cleanup queries
        builder.HasIndex(r => r.ExpiresAtUtc);
    }
}
