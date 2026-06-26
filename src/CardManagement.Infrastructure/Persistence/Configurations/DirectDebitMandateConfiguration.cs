using CardManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for DirectDebitMandate.
/// </summary>
public class DirectDebitMandateConfiguration : IEntityTypeConfiguration<DirectDebitMandate>
{
    public void Configure(EntityTypeBuilder<DirectDebitMandate> builder)
    {
        builder.ToTable("DirectDebitMandates");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .ValueGeneratedNever();

        builder.Property(d => d.MandateReference)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(d => d.MandateReference)
            .IsUnique();

        builder.Property(d => d.DebtorAccount)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.CreditorAccount)
            .HasMaxLength(64)
            .IsRequired();

        builder.OwnsOne(d => d.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").IsRequired();
            money.Property(m => m.CurrencyCode).HasColumnName("CurrencyCode").HasMaxLength(3).IsRequired();
        });

        builder.Property(d => d.Frequency)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(d => d.StartDate)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(d => d.EndDate)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(d => d.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(d => d.NibssReference)
            .HasMaxLength(128)
            .IsRequired(false);

        builder.HasIndex(d => d.Status);
    }
}
