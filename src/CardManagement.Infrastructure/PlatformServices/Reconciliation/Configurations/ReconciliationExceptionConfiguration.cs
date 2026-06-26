using CardManagement.Domain.PlatformServices.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.Reconciliation.Configurations;

/// <summary>
/// EF Core entity configuration for ReconciliationException.
/// </summary>
public class ReconciliationExceptionConfiguration : IEntityTypeConfiguration<ReconciliationException>
{
    public void Configure(EntityTypeBuilder<ReconciliationException> builder)
    {
        builder.ToTable("ReconciliationExceptions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.BatchId)
            .IsRequired();

        builder.HasIndex(e => e.BatchId);

        builder.Property(e => e.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.SettlementLineItemId);

        builder.Property(e => e.PaymentRequestId);

        builder.OwnsOne(e => e.ExternalAmount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("ExternalAmount");
            money.Property(m => m.CurrencyCode).HasColumnName("ExternalCurrencyCode").HasMaxLength(3);
        });

        builder.OwnsOne(e => e.InternalAmount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("InternalAmount");
            money.Property(m => m.CurrencyCode).HasColumnName("InternalCurrencyCode").HasMaxLength(3);
        });

        builder.Property(e => e.ExternalStatus)
            .HasMaxLength(64);

        builder.Property(e => e.InternalStatus)
            .HasMaxLength(64);

        builder.Property(e => e.ResolutionStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.AdjustmentId);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.HasIndex(e => e.ResolutionStatus);
        builder.HasIndex(e => e.Type);
    }
}
