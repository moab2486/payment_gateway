using CardManagement.Domain.PlatformServices.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.PlatformServices.Notifications.Persistence;

/// <summary>
/// EF Core entity configuration for NotificationTemplate.
/// </summary>
public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("NotificationTemplates");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedNever();

        builder.Property(t => t.Name)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(t => t.Name)
            .IsUnique()
            .HasDatabaseName("IX_NotificationTemplates_Name");

        builder.Property(t => t.Category)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(t => t.Category)
            .HasDatabaseName("IX_NotificationTemplates_Category");

        builder.Property(t => t.EmailSubjectTemplate)
            .HasMaxLength(1024)
            .IsRequired(false);

        builder.Property(t => t.EmailBodyTemplate)
            .IsRequired(false);

        builder.Property(t => t.SmsBodyTemplate)
            .HasMaxLength(1600)
            .IsRequired(false);

        builder.Property(t => t.WhatsAppBodyTemplate)
            .HasMaxLength(4096)
            .IsRequired(false);

        builder.Property(t => t.RequiredVariables)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(t => t.Version)
            .IsRequired();

        builder.Property(t => t.UpdatedAtUtc)
            .IsRequired()
            .HasPrecision(3);
    }
}
