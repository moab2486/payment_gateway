using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.AccountNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(a => a.AccountNumber)
            .IsUnique();

        builder.Property(a => a.AccountType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.Currency)
            .IsRequired()
            .HasMaxLength(3); // ISO 4217

        builder.Property(a => a.Balance)
            .IsRequired();

        builder.Property(a => a.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(a => a.UpdatedAtUtc)
            .IsRequired()
            .HasPrecision(3);
    }
}
