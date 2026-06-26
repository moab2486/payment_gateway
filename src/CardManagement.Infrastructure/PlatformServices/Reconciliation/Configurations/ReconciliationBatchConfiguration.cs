using CardManagement.Domain.PlatformServices.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.Reconciliation.Configurations;

/// <summary>
/// EF Core entity configuration for ReconciliationBatch.
/// </summary>
public class ReconciliationBatchConfiguration : IEntityTypeConfiguration<ReconciliationBatch>
{
    public void Configure(EntityTypeBuilder<ReconciliationBatch> builder)
    {
        builder.ToTable("ReconciliationBatches");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .ValueGeneratedNever();

        builder.Property(b => b.Processor)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(b => b.SettlementDate)
            .IsRequired();

        builder.Property(b => b.FileHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(b => b.FileHash)
            .IsUnique();

        builder.Property(b => b.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(b => b.TotalRows)
            .IsRequired();

        builder.Property(b => b.ParsedRows)
            .IsRequired();

        builder.Property(b => b.ErrorRows)
            .IsRequired();

        builder.Property(b => b.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(b => b.CompletedAtUtc)
            .HasPrecision(3);

        builder.Property(b => b.CreatedBy)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(b => b.Status);
        builder.HasIndex(b => b.SettlementDate);
    }
}
