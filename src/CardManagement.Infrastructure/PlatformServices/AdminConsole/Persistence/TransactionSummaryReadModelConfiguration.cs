using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole.Persistence;

/// <summary>
/// EF Core entity configuration for TransactionSummaryReadModel.
/// Optimized for dashboard queries with composite key and date/channel indexes.
/// </summary>
public class TransactionSummaryReadModelConfiguration : IEntityTypeConfiguration<TransactionSummaryReadModel>
{
    public void Configure(EntityTypeBuilder<TransactionSummaryReadModel> builder)
    {
        builder.ToTable("AdminTransactionSummaries");

        // Composite key: Date + Channel (one record per date per channel)
        builder.HasKey(t => new { t.Date, t.Channel });

        builder.Property(t => t.Date)
            .IsRequired();

        builder.Property(t => t.Channel)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(t => t.TotalCount)
            .IsRequired();

        builder.Property(t => t.SuccessCount)
            .IsRequired();

        builder.Property(t => t.FailedCount)
            .IsRequired();

        builder.Property(t => t.TotalAmountKobo)
            .IsRequired();

        builder.Property(t => t.ProjectedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        // Index for date-range queries on dashboard
        builder.HasIndex(t => t.Date)
            .HasDatabaseName("IX_AdminTransactionSummaries_Date");

        builder.HasIndex(t => t.Channel)
            .HasDatabaseName("IX_AdminTransactionSummaries_Channel");
    }
}
