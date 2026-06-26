using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole.Persistence;

/// <summary>
/// EF Core entity configuration for ReconciliationStatusReadModel.
/// Uses BatchId as primary key since each batch has one status projection.
/// </summary>
public class ReconciliationStatusReadModelConfiguration : IEntityTypeConfiguration<ReconciliationStatusReadModel>
{
    public void Configure(EntityTypeBuilder<ReconciliationStatusReadModel> builder)
    {
        builder.ToTable("AdminReconciliationStatuses");

        builder.HasKey(r => r.BatchId);

        builder.Property(r => r.BatchId)
            .ValueGeneratedNever();

        builder.Property(r => r.Processor)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.SettlementDate)
            .IsRequired();

        builder.Property(r => r.TotalRecords)
            .IsRequired();

        builder.Property(r => r.MatchedRecords)
            .IsRequired();

        builder.Property(r => r.ExceptionCount)
            .IsRequired();

        builder.Property(r => r.ResolvedCount)
            .IsRequired();

        builder.Property(r => r.ProjectedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        // Indexes for dashboard filtering
        builder.HasIndex(r => r.Processor)
            .HasDatabaseName("IX_AdminReconciliationStatuses_Processor");

        builder.HasIndex(r => r.SettlementDate)
            .HasDatabaseName("IX_AdminReconciliationStatuses_SettlementDate");
    }
}
