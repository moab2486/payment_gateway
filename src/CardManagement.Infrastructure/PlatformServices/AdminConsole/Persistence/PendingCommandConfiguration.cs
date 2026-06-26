using CardManagement.Domain.PlatformServices.AdminConsole;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole.Persistence;

/// <summary>
/// EF Core entity configuration for PendingCommand.
/// Maps the maker-checker command entity to the AdminPendingCommands table.
/// </summary>
public class PendingCommandConfiguration : IEntityTypeConfiguration<PendingCommand>
{
    public void Configure(EntityTypeBuilder<PendingCommand> builder)
    {
        builder.ToTable("AdminPendingCommands");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .ValueGeneratedNever();

        builder.Property(c => c.CommandType)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(c => c.SerializedParameters)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(c => c.MakerId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(c => c.CheckerId)
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(c => c.RejectionReason)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.Property(c => c.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(c => c.ResolvedAtUtc)
            .IsRequired(false)
            .HasPrecision(3);

        builder.Property(c => c.ExpiresAtUtc)
            .IsRequired()
            .HasPrecision(3);

        // Indexes for dashboard queries
        builder.HasIndex(c => c.Status)
            .HasDatabaseName("IX_AdminPendingCommands_Status");

        builder.HasIndex(c => c.MakerId)
            .HasDatabaseName("IX_AdminPendingCommands_MakerId");

        builder.HasIndex(c => c.ExpiresAtUtc)
            .HasDatabaseName("IX_AdminPendingCommands_ExpiresAtUtc");

        builder.HasIndex(c => c.CommandType)
            .HasDatabaseName("IX_AdminPendingCommands_CommandType");
    }
}
