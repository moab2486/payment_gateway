using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Generators;

/// <summary>
/// Smoke tests that verify all FsCheck generators produce valid instances.
/// </summary>
public class GeneratorSmokeTests
{
    [Property(Arbitrary = new[] { typeof(ReconciliationGenerators.ReconciliationArbitraries) })]
    public bool SettlementLineItem_HasNonEmptyReferences(SettlementLineItem item)
    {
        return !string.IsNullOrWhiteSpace(item.TransactionReference)
            && !string.IsNullOrWhiteSpace(item.ProcessorReference)
            && item.Amount.Amount > 0
            && item.Id != Guid.Empty;
    }

    [Property(Arbitrary = new[] { typeof(ReconciliationGenerators.ReconciliationArbitraries) })]
    public bool PaymentRequest_HasValidMoney(PaymentRequest request)
    {
        return request.Amount.Amount > 0
            && request.Amount.CurrencyCode.Length == 3
            && !string.IsNullOrWhiteSpace(request.TransactionReference);
    }

    [Property(Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public bool WebhookSubscription_HasValidUrl(WebhookSubscriptionData subscription)
    {
        return !string.IsNullOrWhiteSpace(subscription.DestinationUrl)
            && subscription.DestinationUrl.StartsWith("https://")
            && subscription.EventTypes.Length > 0
            && !string.IsNullOrWhiteSpace(subscription.SigningSecret);
    }

    [Property(Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public bool WebhookPayload_HasRequiredFields(WebhookPayloadData payload)
    {
        return payload.DeliveryId != Guid.Empty
            && !string.IsNullOrWhiteSpace(payload.EventType)
            && !string.IsNullOrWhiteSpace(payload.PayloadBody);
    }

    [Property(Arbitrary = new[] { typeof(NotificationGenerators.NotificationArbitraries) })]
    public bool NotificationTemplate_HasRequiredFields(NotificationTemplateData template)
    {
        return !string.IsNullOrWhiteSpace(template.Name)
            && !string.IsNullOrWhiteSpace(template.Category)
            && template.RequiredVariables.Length > 0
            && template.Version >= 1;
    }

    [Property(Arbitrary = new[] { typeof(DeveloperPortalGenerators.DeveloperPortalArbitraries) })]
    public bool AdminCommand_HasRequiredFields(AdminCommandData command)
    {
        return command.Id != Guid.Empty
            && !string.IsNullOrWhiteSpace(command.CommandType)
            && !string.IsNullOrWhiteSpace(command.MakerId)
            && !string.IsNullOrWhiteSpace(command.SerializedParameters);
    }

    [Property(Arbitrary = new[] { typeof(DeveloperPortalGenerators.DeveloperPortalArbitraries) })]
    public bool ApiKey_HasValidHash(ApiKeyData apiKey)
    {
        return apiKey.Id != Guid.Empty
            && apiKey.DeveloperId != Guid.Empty
            && !string.IsNullOrWhiteSpace(apiKey.KeyHash)
            && apiKey.KeyHash.Length == 64 // SHA-256 hex encoded
            && !string.IsNullOrWhiteSpace(apiKey.KeyPrefix)
            && apiKey.KeyPrefix.Length == 8
            && apiKey.Scopes.Length > 0;
    }
}
