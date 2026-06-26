using FsCheck;

namespace CardManagement.PlatformServices.UnitTests.Generators;

/// <summary>
/// FsCheck generators for Notification domain entities.
/// </summary>
public static class NotificationGenerators
{
    private static readonly string[] Categories = { "payment", "dispute", "account", "security", "marketing" };

    private static readonly string[] VariableNames =
    {
        "recipient_name", "amount", "currency", "transaction_id",
        "merchant_name", "date", "status", "reference_number"
    };

    public static Arbitrary<NotificationTemplateData> NotificationTemplateArbitrary()
    {
        return (from id in Arb.Generate<Guid>()
                from name in Gen.Elements("payment_success", "payment_failed", "dispute_created",
                    "refund_processed", "account_locked", "password_reset")
                from category in Gen.Elements(Categories)
                from hasEmail in Arb.Generate<bool>()
                from hasSms in Arb.Generate<bool>()
                from hasWhatsApp in Arb.Generate<bool>()
                from varCount in Gen.Choose(1, 4)
                from vars in Gen.ArrayOf(varCount, Gen.Elements(VariableNames))
                    .Select(arr => arr.Distinct().ToArray())
                from version in Gen.Choose(1, 10)
                let emailSubject = hasEmail ? $"[{{{{merchant_name}}}}] Notification - {name}" : null
                let emailBody = hasEmail ? $"Dear {{{{recipient_name}}}}, your {name} for {{{{amount}}}} {{{{currency}}}} is confirmed." : null
                let smsBody = hasSms ? $"{{{{recipient_name}}}}: {name} - {{{{amount}}}} {{{{currency}}}}. Ref: {{{{transaction_id}}}}" : null
                let whatsAppBody = hasWhatsApp ? $"Hi {{{{recipient_name}}}}! Your {name} of {{{{amount}}}} {{{{currency}}}} has been processed." : null
                select new NotificationTemplateData(
                    id,
                    name,
                    category,
                    emailSubject,
                    emailBody,
                    smsBody,
                    whatsAppBody,
                    vars,
                    version))
            .ToArbitrary();
    }

    /// <summary>
    /// Registers all notification-related arbitraries with FsCheck.
    /// </summary>
    public class NotificationArbitraries
    {
        public static Arbitrary<NotificationTemplateData> NotificationTemplates() => NotificationTemplateArbitrary();
    }
}

/// <summary>
/// Data record for notification template generation in property-based tests.
/// Mirrors the NotificationTemplate domain entity structure.
/// </summary>
public record NotificationTemplateData(
    Guid Id,
    string Name,
    string Category,
    string? EmailSubjectTemplate,
    string? EmailBodyTemplate,
    string? SmsBodyTemplate,
    string? WhatsAppBodyTemplate,
    string[] RequiredVariables,
    int Version);
