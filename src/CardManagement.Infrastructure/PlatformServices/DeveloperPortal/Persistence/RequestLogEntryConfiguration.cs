using CardManagement.Domain.PlatformServices.DeveloperPortal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.DeveloperPortal.Persistence;

/// <summary>
/// EF Core entity configuration for RequestLogEntry.
/// Stores PCI-redacted API request/response metadata for developer debugging.
/// </summary>
public class RequestLogEntryConfiguration : IEntityTypeConfiguration<RequestLogEntry>
{
    public void Configure(EntityTypeBuilder<RequestLogEntry> builder)
    {
        builder.ToTable("DeveloperRequestLogs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.ApiKeyId)
            .IsRequired();

        builder.HasIndex(e => e.ApiKeyId)
            .HasDatabaseName("IX_DeveloperRequestLogs_ApiKeyId");

        builder.Property(e => e.DeveloperId)
            .IsRequired();

        builder.HasIndex(e => e.DeveloperId)
            .HasDatabaseName("IX_DeveloperRequestLogs_DeveloperId");

        builder.Property(e => e.Endpoint)
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(e => e.Method)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(e => e.RequestHeaders)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(e => e.RequestBody)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(e => e.ResponseStatus)
            .IsRequired();

        builder.HasIndex(e => e.ResponseStatus)
            .HasDatabaseName("IX_DeveloperRequestLogs_ResponseStatus");

        builder.Property(e => e.ResponseBody)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(e => e.Latency)
            .IsRequired();

        builder.Property(e => e.TimestampUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.HasIndex(e => e.TimestampUtc)
            .HasDatabaseName("IX_DeveloperRequestLogs_TimestampUtc");

        // Composite index for developer-scoped queries with time-based filtering
        builder.HasIndex(e => new { e.DeveloperId, e.TimestampUtc })
            .HasDatabaseName("IX_DeveloperRequestLogs_DeveloperId_TimestampUtc");
    }
}
