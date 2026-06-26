using CardManagement.Domain.PlatformServices.Notifications;
using FsCheck;

namespace CardManagement.PlatformServices.UnitTests.Properties.Notifications;

/// <summary>
/// Test data record representing a channel body variant with a unique marker
/// that enables verifying channel isolation in property tests.
/// </summary>
public record ChannelBodyVariant(string Template, string UniqueMarker);

/// <summary>
/// Custom FsCheck generators for template rendering property tests.
/// </summary>
public static class TemplateRenderingGenerators
{
    private static readonly string[] VariableNames =
    {
        "recipient_name", "amount", "currency", "transaction_id",
        "merchant_name", "date", "status", "reference_number"
    };

    /// <summary>
    /// Generates a NotificationTemplate with all required variables present in the variable map.
    /// Returns (template, channel to render for, complete variable map).
    /// </summary>
    public static Arbitrary<(NotificationTemplate Template, NotificationChannel Channel, Dictionary<string, string> Variables)>
        TemplateWithCompleteVariables()
    {
        var gen = from varCount in Gen.Choose(1, 5)
                  from selectedVars in Gen.ArrayOf(varCount, Gen.Elements(VariableNames))
                      .Select(arr => arr.Distinct().ToArray())
                  where selectedVars.Length > 0
                  from channel in Gen.Elements(
                      NotificationChannel.Email,
                      NotificationChannel.Sms,
                      NotificationChannel.WhatsApp)
                  from values in Gen.Sequence(
                      selectedVars.Select(_ => Gen.Elements(
                          "John Doe", "5000", "NGN", "TXN-123456",
                          "Acme Inc", "2024-01-15", "completed", "REF-789")))
                  let variableMap = selectedVars.Zip(values)
                      .ToDictionary(x => x.First, x => x.Second)
                  let templateBodies = BuildChannelTemplates(selectedVars, channel)
                  let template = NotificationTemplate.Create(
                      name: $"test_template_{varCount}",
                      category: "payment",
                      requiredVariables: selectedVars,
                      emailSubjectTemplate: templateBodies.emailSubject,
                      emailBodyTemplate: templateBodies.emailBody,
                      smsBodyTemplate: templateBodies.smsBody,
                      whatsAppBodyTemplate: templateBodies.whatsAppBody)
                  select (template, channel, variableMap);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a template with required variables and an incomplete variable map
    /// where exactly one variable is missing.
    /// Returns (template, channel, incomplete variable map, name of missing variable).
    /// </summary>
    public static Arbitrary<(NotificationTemplate Template, NotificationChannel Channel, Dictionary<string, string> IncompleteVariables, string MissingVariable)>
        TemplateWithMissingVariable()
    {
        var gen = from varCount in Gen.Choose(2, 5)
                  from selectedVars in Gen.ArrayOf(varCount, Gen.Elements(VariableNames))
                      .Select(arr => arr.Distinct().ToArray())
                  where selectedVars.Length >= 2
                  from missingIndex in Gen.Choose(0, selectedVars.Length - 1)
                  from channel in Gen.Elements(
                      NotificationChannel.Email,
                      NotificationChannel.Sms,
                      NotificationChannel.WhatsApp)
                  from values in Gen.Sequence(
                      selectedVars.Select(_ => Gen.Elements(
                          "Jane Smith", "10000", "USD", "TXN-999",
                          "Widget Corp", "2024-03-20", "pending", "REF-456")))
                  let allVariables = selectedVars.Zip(values)
                      .ToDictionary(x => x.First, x => x.Second)
                  let missingVariable = selectedVars[missingIndex]
                  let incompleteVariables = allVariables
                      .Where(kv => kv.Key != missingVariable)
                      .ToDictionary(kv => kv.Key, kv => kv.Value)
                  let templateBodies = BuildChannelTemplates(selectedVars, channel)
                  let template = NotificationTemplate.Create(
                      name: $"test_template_missing_{varCount}",
                      category: "payment",
                      requiredVariables: selectedVars,
                      emailSubjectTemplate: templateBodies.emailSubject,
                      emailBodyTemplate: templateBodies.emailBody,
                      smsBodyTemplate: templateBodies.smsBody,
                      whatsAppBodyTemplate: templateBodies.whatsAppBody)
                  select (template, channel, incompleteVariables, missingVariable);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a template with all three channel variants (email, SMS, WhatsApp),
    /// each containing a unique marker that enables verification of channel isolation.
    /// Returns (template, complete variables, email body info, sms body info, whatsApp body info).
    /// </summary>
    public static Arbitrary<(NotificationTemplate Template, Dictionary<string, string> Variables, ChannelBodyVariant EmailBody, ChannelBodyVariant SmsBody, ChannelBodyVariant WhatsAppBody)>
        TemplateWithAllChannelVariants()
    {
        var gen = from varCount in Gen.Choose(1, 3)
                  from selectedVars in Gen.ArrayOf(varCount, Gen.Elements(VariableNames))
                      .Select(arr => arr.Distinct().ToArray())
                  where selectedVars.Length > 0
                  from emailMarker in Gen.Elements("EMAIL_MARKER_A", "EMAIL_MARKER_B", "EMAIL_MARKER_C")
                  from smsMarker in Gen.Elements("SMS_MARKER_X", "SMS_MARKER_Y", "SMS_MARKER_Z")
                  from whatsAppMarker in Gen.Elements("WHATSAPP_MARKER_1", "WHATSAPP_MARKER_2", "WHATSAPP_MARKER_3")
                  from values in Gen.Sequence(
                      selectedVars.Select(_ => Gen.Elements(
                          "Alice", "2500", "GBP", "TXN-ABC",
                          "Store Ltd", "2024-06-01", "success", "REF-XYZ")))
                  let variableMap = selectedVars.Zip(values)
                      .ToDictionary(x => x.First, x => x.Second)
                  let placeholders = string.Join(" ", selectedVars.Select(v => $"{{{{{v}}}}}"))
                  let emailSubjectTemplate = $"[{emailMarker}] Subject {placeholders}"
                  let emailBodyTemplate = $"[{emailMarker}] Email body: {placeholders}"
                  let smsBodyTemplate = $"[{smsMarker}] SMS body: {placeholders}"
                  let whatsAppBodyTemplate = $"[{whatsAppMarker}] WhatsApp body: {placeholders}"
                  let template = NotificationTemplate.Create(
                      name: "multi_channel_template",
                      category: "payment",
                      requiredVariables: selectedVars,
                      emailSubjectTemplate: emailSubjectTemplate,
                      emailBodyTemplate: emailBodyTemplate,
                      smsBodyTemplate: smsBodyTemplate,
                      whatsAppBodyTemplate: whatsAppBodyTemplate)
                  select (
                      template,
                      variableMap,
                      new ChannelBodyVariant(emailBodyTemplate, emailMarker),
                      new ChannelBodyVariant(smsBodyTemplate, smsMarker),
                      new ChannelBodyVariant(whatsAppBodyTemplate, whatsAppMarker))
                  ;

        return gen.ToArbitrary();
    }

    private static (string? emailSubject, string? emailBody, string? smsBody, string? whatsAppBody)
        BuildChannelTemplates(string[] variables, NotificationChannel targetChannel)
    {
        var placeholders = string.Join(" ", variables.Select(v => $"{{{{{v}}}}}"));

        // Always create the target channel's template, and optionally others
        string? emailSubject = null;
        string? emailBody = null;
        string? smsBody = null;
        string? whatsAppBody = null;

        switch (targetChannel)
        {
            case NotificationChannel.Email:
                emailSubject = $"Notification: {placeholders}";
                emailBody = $"Dear user, {placeholders}. Thank you.";
                break;
            case NotificationChannel.Sms:
                smsBody = $"Alert: {placeholders}";
                break;
            case NotificationChannel.WhatsApp:
                whatsAppBody = $"Hi! {placeholders} - sent via WhatsApp.";
                break;
        }

        return (emailSubject, emailBody, smsBody, whatsAppBody);
    }
}
