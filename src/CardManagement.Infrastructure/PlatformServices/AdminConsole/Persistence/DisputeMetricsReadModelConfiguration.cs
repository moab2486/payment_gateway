using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole.Persistence;

/// <summary>
/// EF Core entity configuration for DisputeMetricsReadModel.
/// Composite key on Date + DisputeType for aggregated dispute metrics dashboard.
/// </summary>
public class DisputeMetricsReadModelConfiguration : IEntityTypeConfiguration<DisputeMetricsReadModel>
{
    public void Configure(EntityTypeBuilder<DisputeMetricsReadModel> builder)
    {
        builder.ToTable("AdminDisputeMetrics");

        // Composite key: Date + DisputeType (one record per date per dispute type)
        builder.HasKey(d => new { d.Date, d.DisputeType });

        builder.Property(d => d.Date)
            .IsRequired();

        builder.Property(d => d.DisputeType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.OpenedCount)
            .IsRequired();

        builder.Property(d => d.ResolvedCount)
            .IsRequired();

        builder.Property(d => d.EscalatedCount)
            .IsRequired();

        builder.Property(d => d.TotalDisputedAmountKobo)
            .IsRequired();

        builder.Property(d => d.AverageResolutionHours)
            .IsRequired();

        builder.Property(d => d.ProjectedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        // Index for date-range queries
        builder.HasIndex(d => d.Date)
            .HasDatabaseName("IX_AdminDisputeMetrics_Date");
    }
}
