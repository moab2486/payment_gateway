using CardManagement.Application.PlatformServices.Notifications;
using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;
using CardManagement.PlatformServices.UnitTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using System.Text.RegularExpressions;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Properties.Notifications;

/// <summary>
/// Property-based tests for template rendering (Properties 18, 19, 20).
/// Uses a simple in-memory ITemplateRenderer implementation for testing.
/// </summary>
public class TemplateRendererPropertyTests
{
    private readonly InMemoryTemplateRenderer _renderer = new();

    /// <summary>
    /// Property 18: Template Rendering Substitution
    /// Template with N placeholders + complete variable map produces output with all values substituted
    /// and zero unresolved {{...}} patterns.
    /// **Validates: Requirements 8.1, 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TemplateRenderingSubstitution_CompleteVariables_NoUnresolvedPlaceholders()
    {
        return Prop.ForAll(
            TemplateRenderingGenerators.TemplateWithCompleteVariables(),
            testCase =>
            {
                var (template, channel, variables) = testCase;

                var rendered = _renderer.Render(
                    template,
                    channel,
                    "recipient-001",
                    GetAddressForChannel(channel),
                    variables);

                // No unresolved {{...}} patterns remain in the output
                var unresolvedPattern = new Regex(@"\{\{[^}]+\}\}");
                var bodyHasUnresolved = unresolvedPattern.IsMatch(rendered.Body);
                var subjectHasUnresolved = rendered.Subject != null && unresolvedPattern.IsMatch(rendered.Subject);

                // All variable values appear in the rendered output
                var allValuesPresent = variables.All(kv =>
                    rendered.Body.Contains(kv.Value) ||
                    (rendered.Subject != null && rendered.Subject.Contains(kv.Value)));

                return (!bodyHasUnresolved && !subjectHasUnresolved && allValuesPresent)
                    .Label($"Body unresolved: {bodyHasUnresolved}, Subject unresolved: {subjectHasUnresolved}, All values present: {allValuesPresent}");
            });
    }

    /// <summary>
    /// Property 19: Template Missing Variable Rejection
    /// Template with missing required variable rejects dispatch and identifies the missing variable name.
    /// **Validates: Requirements 8.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TemplateMissingVariableRejection_IncompleteMap_ThrowsWithMissingVariableName()
    {
        return Prop.ForAll(
            TemplateRenderingGenerators.TemplateWithMissingVariable(),
            testCase =>
            {
                var (template, channel, incompleteVariables, missingVariable) = testCase;

                try
                {
                    _renderer.Render(
                        template,
                        channel,
                        "recipient-001",
                        GetAddressForChannel(channel),
                        incompleteVariables);

                    // Should not reach here - rendering should reject
                    return false.Label("Expected exception for missing variable but rendering succeeded");
                }
                catch (TemplateRenderingException ex)
                {
                    // The exception should identify the missing variable name
                    return ex.MissingVariable.Equals(missingVariable)
                        .Label($"Expected missing variable '{missingVariable}' but got '{ex.MissingVariable}'");
                }
            });
    }

    /// <summary>
    /// Property 20: Template Channel-Specific Rendering
    /// Rendering for a specific channel returns that channel's variant content, not another channel's.
    /// **Validates: Requirements 8.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TemplateChannelSpecificRendering_ReturnsCorrectChannelContent()
    {
        return Prop.ForAll(
            TemplateRenderingGenerators.TemplateWithAllChannelVariants(),
            testCase =>
            {
                var (template, variables, emailBody, smsBody, whatsAppBody) = testCase;

                var emailRendered = _renderer.Render(
                    template,
                    NotificationChannel.Email,
                    "recipient-001",
                    "user@example.com",
                    variables);

                var smsRendered = _renderer.Render(
                    template,
                    NotificationChannel.Sms,
                    "recipient-001",
                    "+2348012345678",
                    variables);

                var whatsAppRendered = _renderer.Render(
                    template,
                    NotificationChannel.WhatsApp,
                    "recipient-001",
                    "+2348012345678",
                    variables);

                // Each channel should render its own content (after substitution)
                // Verify that the rendered body does NOT contain the unique marker from another channel
                var emailCorrect = emailRendered.Body.Contains(emailBody.UniqueMarker);
                var smsCorrect = smsRendered.Body.Contains(smsBody.UniqueMarker);
                var whatsAppCorrect = whatsAppRendered.Body.Contains(whatsAppBody.UniqueMarker);

                // Ensure no cross-channel content
                var emailNoSms = !emailRendered.Body.Contains(smsBody.UniqueMarker);
                var emailNoWhatsApp = !emailRendered.Body.Contains(whatsAppBody.UniqueMarker);
                var smsNoEmail = !smsRendered.Body.Contains(emailBody.UniqueMarker);
                var smsNoWhatsApp = !smsRendered.Body.Contains(whatsAppBody.UniqueMarker);
                var whatsAppNoEmail = !whatsAppRendered.Body.Contains(emailBody.UniqueMarker);
                var whatsAppNoSms = !whatsAppRendered.Body.Contains(smsBody.UniqueMarker);

                return (emailCorrect && smsCorrect && whatsAppCorrect &&
                        emailNoSms && emailNoWhatsApp &&
                        smsNoEmail && smsNoWhatsApp &&
                        whatsAppNoEmail && whatsAppNoSms)
                    .Label($"Email:{emailCorrect}, SMS:{smsCorrect}, WhatsApp:{whatsAppCorrect}, " +
                           $"Cross-channel isolation: Email(!SMS:{emailNoSms}, !WA:{emailNoWhatsApp}), " +
                           $"SMS(!Email:{smsNoEmail}, !WA:{smsNoWhatsApp}), " +
                           $"WA(!Email:{whatsAppNoEmail}, !SMS:{whatsAppNoSms})");
            });
    }

    private static string GetAddressForChannel(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => "user@example.com",
        NotificationChannel.Sms => "+2348012345678",
        NotificationChannel.WhatsApp => "+2348012345678",
        _ => "unknown"
    };
}

/// <summary>
/// Simple in-memory ITemplateRenderer implementation for property-based testing.
/// Replaces {{variable_name}} placeholders with values from the variable map.
/// Throws TemplateRenderingException if required variables are missing.
/// Selects channel-specific content based on the requested channel.
/// </summary>
public class InMemoryTemplateRenderer : ITemplateRenderer
{
    private static readonly Regex PlaceholderPattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    public RenderedNotification Render(
        NotificationTemplate template,
        NotificationChannel channel,
        string recipientId,
        string recipientAddress,
        Dictionary<string, string> variables)
    {
        // Select channel-specific template content
        var (subject, body) = channel switch
        {
            NotificationChannel.Email => (template.EmailSubjectTemplate, template.EmailBodyTemplate),
            NotificationChannel.Sms => (null, template.SmsBodyTemplate),
            NotificationChannel.WhatsApp => (null, template.WhatsAppBodyTemplate),
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };

        if (body is null)
            throw new InvalidOperationException($"Template does not support channel '{channel}'.");

        // Validate all required variables are provided and substitute
        var renderedBody = SubstitutePlaceholders(body, variables);
        var renderedSubject = subject != null ? SubstitutePlaceholders(subject, variables) : null;

        return new RenderedNotification(
            body: renderedBody,
            channel: channel,
            recipientAddress: recipientAddress,
            templateId: template.Id,
            recipientId: recipientId,
            subject: renderedSubject);
    }

    private static string SubstitutePlaceholders(string templateContent, Dictionary<string, string> variables)
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
