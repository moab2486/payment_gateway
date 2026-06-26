using System.Text.Json;
using CardManagement.Domain.PlatformServices.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.Notifications.Persistence;

/// <summary>
/// EF Core entity configuration for NotificationPreference.
/// </summary>
public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.RecipientId)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(p => p.RecipientId)
            .IsUnique()
            .HasDatabaseName("IX_NotificationPreferences_RecipientId");

        builder.Property(p => p.PrimaryChannel)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.FallbackChannel)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired(false);

        builder.Property(p => p.CategoryOptIn)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, bool>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, bool>())
            .HasColumnType("jsonb")
            .IsRequired();
    }
}
