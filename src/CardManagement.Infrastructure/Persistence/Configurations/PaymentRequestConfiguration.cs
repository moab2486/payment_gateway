using CardManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for PaymentRequest.
/// </summary>
public class PaymentRequestConfiguration : IEntityTypeConfiguration<PaymentRequest>
{
    public void Configure(EntityTypeBuilder<PaymentRequest> builder)
    {
        builder.ToTable("PaymentRequests");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.IdempotencyKey)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(p => p.IdempotencyKey)
            .IsUnique();

        builder.Property(p => p.TransactionReference)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(p => p.TransactionReference)
            .IsUnique();

        builder.Property(p => p.TransactionType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.OwnsOne(p => p.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").IsRequired();
            money.Property(m => m.CurrencyCode).HasColumnName("CurrencyCode").HasMaxLength(3).IsRequired();
        });

        builder.Property(p => p.SourceAccount)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(p => p.DestinationAccount)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(p => p.Channel)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.RiskScore);

        builder.Property(p => p.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(p => p.CompletedAtUtc)
            .HasPrecision(3);

        builder.HasIndex(p => p.Status);
    }
}
