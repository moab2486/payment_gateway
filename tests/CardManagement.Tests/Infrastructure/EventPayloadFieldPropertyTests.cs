using System.Text.Json;
using CardManagement.Application.DTOs;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for Event Payload Field Completeness (Property 3).
/// Validates: Requirements 5.4, 5.5, 5.6
/// </summary>
[Trait("Feature", "docker-kafka-integration")]
[Trait("Property", "3")]
public class EventPayloadFieldPropertyTests
{
    /// <summary>
    /// Generator for CardIssuedEvent with all fields populated (non-default values).
    /// </summary>
    private static Gen<CardIssuedEvent> GenCardIssuedEvent()
    {
        return from cardId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
               from cardScheme in Gen.Elements("Visa", "Mastercard", "Verve", "Amex")
               from accountId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
               from year in Gen.Choose(2020, 2030)
               from month in Gen.Choose(1, 12)
               from day in Gen.Choose(1, 28)
               from hour in Gen.Choose(0, 23)
               from minute in Gen.Choose(0, 59)
               from second in Gen.Choose(0, 59)
               from correlationId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
               select new CardIssuedEvent
               {
                   CardId = cardId,
                   CardScheme = cardScheme,
                   AccountId = accountId,
                   IssuanceTimestamp = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc),
                   CorrelationId = correlationId.ToString()
               };
    }

    /// <summary>
    /// Generator for TransactionAuthorizedEvent with all fields populated (non-default values).
    /// </summary>
    private static Gen<TransactionAuthorizedEvent> GenTransactionAuthorizedEvent()
    {
        return from transactionId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
               from cardId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
               from amount in Gen.Choose(1, 1_000_000).Select(a => (long)a)
               from currency in Gen.Elements("USD", "EUR", "GBP", "NGN", "JPY")
               from processorType in Gen.Elements("CardFi", "Interswitch", "Stripe")
               from responseCode in Gen.Elements("00", "01", "05", "12", "51")
               from year in Gen.Choose(2020, 2030)
               from month in Gen.Choose(1, 12)
               from day in Gen.Choose(1, 28)
               from hour in Gen.Choose(0, 23)
               from minute in Gen.Choose(0, 59)
               from second in Gen.Choose(0, 59)
               from correlationId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
               select new TransactionAuthorizedEvent
               {
                   TransactionId = transactionId,
                   CardId = cardId,
                   Amount = amount,
                   Currency = currency,
                   ProcessorType = processorType,
                   ResponseCode = responseCode,
                   AuthorizationTimestamp = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc),
                   CorrelationId = correlationId.ToString()
               };
    }

    /// <summary>
    /// Generator for TransactionReversedEvent with all fields populated (non-default values).
    /// </summary>
    private static Gen<TransactionReversedEvent> GenTransactionReversedEvent()
    {
        return from originalTxId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
               from reversalTxId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
               from amount in Gen.Choose(1, 1_000_000).Select(a => (long)a)
               from currency in Gen.Elements("USD", "EUR", "GBP", "NGN", "JPY")
               from year in Gen.Choose(2020, 2030)
               from month in Gen.Choose(1, 12)
               from day in Gen.Choose(1, 28)
               from hour in Gen.Choose(0, 23)
               from minute in Gen.Choose(0, 59)
               from second in Gen.Choose(0, 59)
               from correlationId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
               select new TransactionReversedEvent
               {
                   OriginalTransactionId = originalTxId,
                   ReversalTransactionId = reversalTxId,
                   Amount = amount,
                   Currency = currency,
                   ReversalTimestamp = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc),
                   CorrelationId = correlationId.ToString()
               };
    }

    /// <summary>
    /// **Validates: Requirements 5.4**
    /// 
    /// Property 3a: For any CardIssuedEvent with all fields populated,
    /// serializing to JSON SHALL produce a document containing all expected fields
    /// (CardId, CardScheme, AccountId, IssuanceTimestamp) as non-null values.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CardIssuedEvent_Serialized_ContainsAllExpectedFields()
    {
        return Prop.ForAll(GenCardIssuedEvent().ToArbitrary(), cardEvent =>
        {
            var json = JsonSerializer.Serialize(cardEvent);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var hasCardId = root.TryGetProperty("CardId", out var cardIdProp)
                && cardIdProp.ValueKind != JsonValueKind.Null;
            var hasCardScheme = root.TryGetProperty("CardScheme", out var cardSchemeProp)
                && cardSchemeProp.ValueKind != JsonValueKind.Null;
            var hasAccountId = root.TryGetProperty("AccountId", out var accountIdProp)
                && accountIdProp.ValueKind != JsonValueKind.Null;
            var hasIssuanceTimestamp = root.TryGetProperty("IssuanceTimestamp", out var issuanceTimestampProp)
                && issuanceTimestampProp.ValueKind != JsonValueKind.Null;

            return (hasCardId && hasCardScheme && hasAccountId && hasIssuanceTimestamp)
                .Label($"CardIssuedEvent JSON missing expected fields. JSON: {json}");
        });
    }

    /// <summary>
    /// **Validates: Requirements 5.5**
    /// 
    /// Property 3b: For any TransactionAuthorizedEvent with all fields populated,
    /// serializing to JSON SHALL produce a document containing all expected fields
    /// (TransactionId, CardId, Amount, Currency, ProcessorType, ResponseCode, AuthorizationTimestamp)
    /// as non-null values.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TransactionAuthorizedEvent_Serialized_ContainsAllExpectedFields()
    {
        return Prop.ForAll(GenTransactionAuthorizedEvent().ToArbitrary(), txEvent =>
        {
            var json = JsonSerializer.Serialize(txEvent);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var hasTransactionId = root.TryGetProperty("TransactionId", out var txIdProp)
                && txIdProp.ValueKind != JsonValueKind.Null;
            var hasCardId = root.TryGetProperty("CardId", out var cardIdProp)
                && cardIdProp.ValueKind != JsonValueKind.Null;
            var hasAmount = root.TryGetProperty("Amount", out var amountProp)
                && amountProp.ValueKind != JsonValueKind.Null;
            var hasCurrency = root.TryGetProperty("Currency", out var currencyProp)
                && currencyProp.ValueKind != JsonValueKind.Null;
            var hasProcessorType = root.TryGetProperty("ProcessorType", out var processorTypeProp)
                && processorTypeProp.ValueKind != JsonValueKind.Null;
            var hasResponseCode = root.TryGetProperty("ResponseCode", out var responseCodeProp)
                && responseCodeProp.ValueKind != JsonValueKind.Null;
            var hasAuthTimestamp = root.TryGetProperty("AuthorizationTimestamp", out var authTimestampProp)
                && authTimestampProp.ValueKind != JsonValueKind.Null;

            return (hasTransactionId && hasCardId && hasAmount && hasCurrency
                    && hasProcessorType && hasResponseCode && hasAuthTimestamp)
                .Label($"TransactionAuthorizedEvent JSON missing expected fields. JSON: {json}");
        });
    }

    /// <summary>
    /// **Validates: Requirements 5.6**
    /// 
    /// Property 3c: For any TransactionReversedEvent with all fields populated,
    /// serializing to JSON SHALL produce a document containing all expected fields
    /// (OriginalTransactionId, ReversalTransactionId, Amount, Currency, ReversalTimestamp)
    /// as non-null values.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TransactionReversedEvent_Serialized_ContainsAllExpectedFields()
    {
        return Prop.ForAll(GenTransactionReversedEvent().ToArbitrary(), txEvent =>
        {
            var json = JsonSerializer.Serialize(txEvent);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var hasOriginalTxId = root.TryGetProperty("OriginalTransactionId", out var origTxIdProp)
                && origTxIdProp.ValueKind != JsonValueKind.Null;
            var hasReversalTxId = root.TryGetProperty("ReversalTransactionId", out var reversalTxIdProp)
                && reversalTxIdProp.ValueKind != JsonValueKind.Null;
            var hasAmount = root.TryGetProperty("Amount", out var amountProp)
                && amountProp.ValueKind != JsonValueKind.Null;
            var hasCurrency = root.TryGetProperty("Currency", out var currencyProp)
                && currencyProp.ValueKind != JsonValueKind.Null;
            var hasReversalTimestamp = root.TryGetProperty("ReversalTimestamp", out var reversalTimestampProp)
                && reversalTimestampProp.ValueKind != JsonValueKind.Null;

            return (hasOriginalTxId && hasReversalTxId && hasAmount && hasCurrency && hasReversalTimestamp)
                .Label($"TransactionReversedEvent JSON missing expected fields. JSON: {json}");
        });
    }
}
