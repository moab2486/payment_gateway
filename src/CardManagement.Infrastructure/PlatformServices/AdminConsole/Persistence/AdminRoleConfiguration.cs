using CardManagement.Domain.PlatformServices.AdminConsole;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole.Persistence;

/// <summary>
/// EF Core entity configuration for AdminRole.
/// Maps admin role assignments to the AdminRoles table.
/// </summary>
public class AdminRoleConfiguration : IEntityTypeConfiguration<AdminRole>
{
    public void Configure(EntityTypeBuilder<AdminRole> builder)
    {
        builder.ToTable("AdminRoles");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.UserId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(r => r.Role)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.AssignedBy)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(r => r.AssignedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(r => r.IsActive)
            .IsRequired();

        // Indexes for RBAC queries
        builder.HasIndex(r => r.UserId)
            .HasDatabaseName("IX_AdminRoles_UserId");

        builder.HasIndex(r => new { r.UserId, r.Role })
            .HasDatabaseName("IX_AdminRoles_UserId_Role");

        builder.HasIndex(r => r.IsActive)
            .HasDatabaseName("IX_AdminRoles_IsActive");
    }
}
