using CardManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for TokenVaultEntity.
/// Stored in the 'pci' schema with restricted access patterns.
/// </summary>
public class TokenVaultEntityConfiguration : IEntityTypeConfiguration<TokenVaultEntity>
{
    public void Configure(EntityTypeBuilder<TokenVaultEntity> builder)
    {
        builder.ToTable("token_vault", "pci");

        builder.HasKey(e => e.Token);

        builder.Property(e => e.Token)
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(e => e.PanHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.EncryptedPan)
            .IsRequired();

        builder.Property(e => e.EncryptedCvv);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.Property(e => e.LastAccessedAtUtc);

        builder.HasIndex(e => e.PanHash)
            .IsUnique()
            .HasDatabaseName("ix_token_vault_pan_hash");
    }
}
