using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole.Persistence;

/// <summary>
/// EF Core entity configuration for ChannelHealthReadModel.
/// Composite key on Date + ChannelName for channel health dashboard.
/// </summary>
public class ChannelHealthReadModelConfiguration : IEntityTypeConfiguration<ChannelHealthReadModel>
{
    public void Configure(EntityTypeBuilder<ChannelHealthReadModel> builder)
    {
        builder.ToTable("AdminChannelHealth");

        // Composite key: Date + ChannelName (one record per date per channel)
        builder.HasKey(h => new { h.Date, h.ChannelName });

        builder.Property(h => h.Date)
            .IsRequired();

        builder.Property(h => h.ChannelName)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(h => h.TotalDeliveries)
            .IsRequired();

        builder.Property(h => h.SuccessfulDeliveries)
            .IsRequired();

        builder.Property(h => h.FailedDeliveries)
            .IsRequired();

        builder.Property(h => h.AverageLatencyMs)
            .IsRequired();

        builder.Property(h => h.UptimePercentage)
            .IsRequired();

        builder.Property(h => h.ProjectedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        // Index for date-range queries
        builder.HasIndex(h => h.Date)
            .HasDatabaseName("IX_AdminChannelHealth_Date");

        builder.HasIndex(h => h.ChannelName)
            .HasDatabaseName("IX_AdminChannelHealth_ChannelName");
    }
}
