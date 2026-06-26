using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.Persistence.Configurations;

public class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.ToTable("Cards");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.PanEncrypted)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(c => c.PanHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(c => c.PanHash)
            .IsUnique();

        builder.Property(c => c.Cvv2Encrypted)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.ExpiryDate)
            .IsRequired()
            .HasMaxLength(5); // MM/YY

        builder.Property(c => c.AccountId)
            .IsRequired();

        builder.Property(c => c.CardScheme)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.BinRange)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(c => c.UpdatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(c => c.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
