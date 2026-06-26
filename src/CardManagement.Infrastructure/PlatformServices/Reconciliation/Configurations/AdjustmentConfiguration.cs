using CardManagement.Domain.PlatformServices.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.Reconciliation.Configurations;

/// <summary>
/// EF Core entity configuration for Adjustment.
/// </summary>
public class AdjustmentConfiguration : IEntityTypeConfiguration<Adjustment>
{
    public void Configure(EntityTypeBuilder<Adjustment> builder)
    {
        builder.ToTable("Adjustments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.ExceptionId)
            .IsRequired();

        builder.HasIndex(a => a.ExceptionId);

        builder.Property(a => a.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.OwnsOne(a => a.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").IsRequired();
            money.Property(m => m.CurrencyCode).HasColumnName("CurrencyCode").HasMaxLength(3).IsRequired();
        });

        builder.Property(a => a.Reason)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(a => a.RuleName)
            .HasMaxLength(256);

        builder.Property(a => a.OperatorId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(a => a.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.HasIndex(a => a.Type);
    }
}
