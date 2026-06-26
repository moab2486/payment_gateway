using CardManagement.Domain.PlatformServices.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.Reconciliation.Configurations;

/// <summary>
/// EF Core entity configuration for SettlementLineItem.
/// </summary>
public class SettlementLineItemConfiguration : IEntityTypeConfiguration<SettlementLineItem>
{
    public void Configure(EntityTypeBuilder<SettlementLineItem> builder)
    {
        builder.ToTable("SettlementLineItems");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.BatchId)
            .IsRequired();

        builder.HasIndex(s => s.BatchId);

        builder.Property(s => s.TransactionReference)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(s => s.TransactionReference);

        builder.Property(s => s.ProcessorReference)
            .HasMaxLength(128)
            .IsRequired();

        builder.OwnsOne(s => s.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").IsRequired();
            money.Property(m => m.CurrencyCode).HasColumnName("CurrencyCode").HasMaxLength(3).IsRequired();
        });

        builder.Property(s => s.Status)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(s => s.TransactionDate)
            .IsRequired();

        builder.Property(s => s.MatchStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(s => s.MatchedPaymentRequestId);

        builder.HasIndex(s => s.MatchStatus);
    }
}
