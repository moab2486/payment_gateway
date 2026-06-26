using CardManagement.Domain.PlatformServices.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks.Persistence;

/// <summary>
/// EF Core entity configuration for WebhookDelivery.
/// </summary>
public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("WebhookDeliveries");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .ValueGeneratedNever();

        builder.Property(d => d.SubscriptionId)
            .IsRequired();

        builder.HasIndex(d => d.SubscriptionId)
            .HasDatabaseName("IX_WebhookDeliveries_SubscriptionId");

        builder.Property(d => d.EventType)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(d => d.Payload)
            .IsRequired();

        builder.Property(d => d.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(d => d.Status)
            .HasDatabaseName("IX_WebhookDeliveries_Status");

        builder.Property(d => d.AttemptCount)
            .IsRequired();

        builder.Property(d => d.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(d => d.NextRetryAtUtc)
            .IsRequired(false)
            .HasPrecision(3);

        builder.Property(d => d.Attempts)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<DeliveryAttempt>>(v, (JsonSerializerOptions?)null) ?? new List<DeliveryAttempt>(),
                new ValueComparer<List<DeliveryAttempt>>(
                    (c1, c2) => JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => JsonSerializer.Deserialize<List<DeliveryAttempt>>(
                        JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null) ?? new List<DeliveryAttempt>()));
    }
}
