using CardManagement.Domain.PlatformServices.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks.Persistence;

/// <summary>
/// EF Core entity configuration for WebhookSubscription.
/// </summary>
public class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.ToTable("WebhookSubscriptions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.MerchantId)
            .IsRequired();

        builder.HasIndex(s => s.MerchantId)
            .HasDatabaseName("IX_WebhookSubscriptions_MerchantId");

        builder.Property(s => s.DestinationUrl)
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(s => s.EventTypes)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(s => s.SigningSecret)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(s => s.Status)
            .HasDatabaseName("IX_WebhookSubscriptions_Status");

        builder.Property(s => s.ConsecutiveFailures)
            .IsRequired();

        builder.Property(s => s.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(s => s.LastDeliveryAtUtc)
            .IsRequired(false)
            .HasPrecision(3);
    }
}
