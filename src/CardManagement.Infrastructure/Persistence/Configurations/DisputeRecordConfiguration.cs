using CardManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for DisputeRecord.
/// </summary>
public class DisputeRecordConfiguration : IEntityTypeConfiguration<DisputeRecord>
{
    public void Configure(EntityTypeBuilder<DisputeRecord> builder)
    {
        builder.ToTable("DisputeRecords");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .ValueGeneratedNever();

        builder.Property(d => d.TransactionReference)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(d => d.TransactionReference)
            .HasDatabaseName("IX_DisputeRecords_TransactionReference");

        builder.Property(d => d.ReasonCode)
            .HasMaxLength(64)
            .IsRequired();

        builder.OwnsOne(d => d.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").IsRequired();
            money.Property(m => m.CurrencyCode).HasColumnName("CurrencyCode").HasMaxLength(3).IsRequired();
        });

        builder.Property(d => d.Evidence)
            .HasMaxLength(4000)
            .IsRequired(false);

        builder.Property(d => d.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(d => d.Status)
            .HasDatabaseName("IX_DisputeRecords_Status");

        builder.Property(d => d.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(d => d.ResolvedAtUtc)
            .IsRequired(false)
            .HasPrecision(3);
    }
}
