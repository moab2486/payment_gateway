using CardManagement.Domain.PlatformServices.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks.Persistence;

/// <summary>
/// EF Core entity configuration for DlqItem (Dead-Letter Queue Item).
/// </summary>
public class DlqItemConfiguration : IEntityTypeConfiguration<DlqItem>
{
    public void Configure(EntityTypeBuilder<DlqItem> builder)
    {
        builder.ToTable("WebhookDlqItems");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .ValueGeneratedNever();

        builder.Property(d => d.DeliveryId)
            .IsRequired();

        builder.HasIndex(d => d.DeliveryId)
            .IsUnique()
            .HasDatabaseName("IX_WebhookDlqItems_DeliveryId");

        builder.Property(d => d.SubscriptionId)
            .IsRequired();

        builder.HasIndex(d => d.SubscriptionId)
            .HasDatabaseName("IX_WebhookDlqItems_SubscriptionId");

        builder.Property(d => d.OriginalPayload)
            .IsRequired();

        builder.Property(d => d.LastError)
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(d => d.MovedToDlqAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(d => d.Replayed)
            .IsRequired();
    }
}
