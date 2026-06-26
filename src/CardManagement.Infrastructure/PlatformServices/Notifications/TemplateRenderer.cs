using System.Text.RegularExpressions;
using CardManagement.Application.PlatformServices.Notifications;
using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;

namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Production implementation of ITemplateRenderer.
/// Performs {{variable_name}} placeholder substitution, selects channel-specific
/// template variants, and validates that all required variables are provided.
/// </summary>
public class TemplateRenderer : ITemplateRenderer
{
    private static readonly Regex PlaceholderPattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    public RenderedNotification Render(
        NotificationTemplate template,
        NotificationChannel channel,
        string recipientId,
        string recipientAddress,
        Dictionary<string, string> variables)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        if (variables is null) throw new ArgumentNullException(nameof(variables));
        if (string.IsNullOrWhiteSpace(recipientId)) throw new ArgumentException("Recipient ID is required.", nameof(recipientId));
        if (string.IsNullOrWhiteSpace(recipientAddress)) throw new ArgumentException("Recipient address is required.", nameof(recipientAddress));

        // Select channel-specific template content
        var (subjectTemplate, bodyTemplate) = SelectChannelContent(template, channel);

        if (bodyTemplate is null)
            throw new InvalidOperationException($"Template '{template.Name}' does not support channel '{channel}'.");

        // Validate all required variables are provided before rendering
        ValidateRequiredVariables(template.RequiredVariables, variables);

        // Substitute placeholders in body and subject
        var renderedBody = SubstitutePlaceholders(bodyTemplate, variables);
        var renderedSubject = subjectTemplate != null ? SubstitutePlaceholders(subjectTemplate, variables) : null;

        return new RenderedNotification(
            body: renderedBody,
            channel: channel,
            recipientAddress: recipientAddress,
            templateId: template.Id,
            recipientId: recipientId,
            subject: renderedSubject);
    }

    private static (string? subject, string? body) SelectChannelContent(
        NotificationTemplate template,
        NotificationChannel channel)
    {
        return channel switch
        {
            NotificationChannel.Email => (template.EmailSubjectTemplate, template.EmailBodyTemplate),
            NotificationChannel.Sms => (null, template.SmsBodyTemplate),
            NotificationChannel.WhatsApp => (null, template.WhatsAppBodyTemplate),
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, $"Unsupported notification channel: {channel}")
        };
    }

    private static void ValidateRequiredVariables(
        string[] requiredVariables,
        Dictionary<string, string> variables)
    {
        foreach (var required in requiredVariables)
        {
            if (!variables.ContainsKey(required))
            {
                throw new TemplateRenderingException(required);
            }
        }
    }

    private static string SubstitutePlaceholders(
        string templateContent,
        Dictionary<string, string> variables)
    {
        return PlaceholderPattern.Replace(templateContent, match =>
        {
            var variableName = match.Groups[1].Value;
            if (!variables.TryGetValue(variableName, out var value))
            {
                throw new TemplateRenderingException(variableName);
            }
            return value;
        });
    }
}
