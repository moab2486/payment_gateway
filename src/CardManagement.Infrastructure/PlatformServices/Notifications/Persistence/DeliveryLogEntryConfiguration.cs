using CardManagement.Domain.PlatformServices.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.Notifications.Persistence;

/// <summary>
/// EF Core entity configuration for DeliveryLogEntry.
/// </summary>
public class DeliveryLogEntryConfiguration : IEntityTypeConfiguration<DeliveryLogEntry>
{
    public void Configure(EntityTypeBuilder<DeliveryLogEntry> builder)
    {
        builder.ToTable("NotificationDeliveryLogs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.RecipientId)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(e => e.RecipientId)
            .HasDatabaseName("IX_NotificationDeliveryLogs_RecipientId");

        builder.Property(e => e.Channel)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(e => e.Channel)
            .HasDatabaseName("IX_NotificationDeliveryLogs_Channel");

        builder.Property(e => e.TemplateId)
            .IsRequired();

        builder.HasIndex(e => e.TemplateId)
            .HasDatabaseName("IX_NotificationDeliveryLogs_TemplateId");

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.DispatchedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.HasIndex(e => e.DispatchedAtUtc)
            .HasDatabaseName("IX_NotificationDeliveryLogs_DispatchedAtUtc");

        builder.Property(e => e.DeliveredAtUtc)
            .IsRequired(false)
            .HasPrecision(3);

        builder.Property(e => e.FailureReason)
            .HasMaxLength(2048)
            .IsRequired(false);

        builder.Property(e => e.ProviderMessageId)
            .HasMaxLength(512)
            .IsRequired(false);
    }
}
