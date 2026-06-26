using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.Persistence.Configurations;

public class TransactionRecordConfiguration : IEntityTypeConfiguration<TransactionRecord>
{
    public void Configure(EntityTypeBuilder<TransactionRecord> builder)
    {
        builder.ToTable("TransactionRecords");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.SystemTraceAuditNumber)
            .IsRequired()
            .HasMaxLength(6);

        builder.HasIndex(t => t.SystemTraceAuditNumber)
            .IsUnique();

        builder.Property(t => t.MessageType)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(t => t.ResponseCode)
            .HasMaxLength(2);

        builder.Property(t => t.CardId)
            .IsRequired();

        builder.Property(t => t.Amount)
            .IsRequired();

        builder.Property(t => t.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(t => t.ProcessorType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(t => t.PipelineStepReached)
            .HasMaxLength(50);

        builder.Property(t => t.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.HasOne<Card>()
            .WithMany()
            .HasForeignKey(t => t.CardId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
