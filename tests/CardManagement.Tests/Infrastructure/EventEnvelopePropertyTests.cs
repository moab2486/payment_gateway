using System.Text.Json;
using CardManagement.Application.DTOs;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for Event Envelope Completeness (Property 2).
/// Validates: Requirements 3.4, 3.7
/// </summary>
[Trait("Feature", "docker-kafka-integration")]
[Trait("Property", "2")]
public class EventEnvelopePropertyTests
{
    private static readonly string[] EventTypes =
    {
        "card-issued",
        "transaction-authorized",
        "transaction-reversed"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Custom FsCheck generator for EventEnvelope instances with valid, non-empty fields.
    /// </summary>
    private static Gen<EventEnvelope> GenEventEnvelope()
    {
        return from eventId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
               from eventTypeIndex in Gen.Choose(0, EventTypes.Length - 1)
               from correlationId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
               from year in Gen.Choose(2020, 2030)
               from month in Gen.Choose(1, 12)
               from day in Gen.Choose(1, 28)
               from hour in Gen.Choose(0, 23)
               from minute in Gen.Choose(0, 59)
               from second in Gen.Choose(0, 59)
               from payloadKey in Gen.Elements("amount", "cardId", "transactionId", "status", "currency")
               from payloadValue in Gen.Elements("100", "USD", "approved", "active", "pending")
               select new EventEnvelope
               {
                   EventId = eventId,
                   EventType = EventTypes[eventTypeIndex],
                   CorrelationId = correlationId.ToString(),
                   Timestamp = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc),
                   Payload = new Dictionary<string, string> { { payloadKey, payloadValue } }
               };
    }

    /// <summary>
    /// **Validates: Requirements 3.4, 3.7**
    ///
    /// Property 2: For any EventEnvelope, serializing to JSON and deserializing back
    /// SHALL produce an envelope with non-empty EventId, non-empty EventType,
    /// non-empty CorrelationId, UTC Timestamp, and non-null Payload.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SerializedEnvelope_DeserializesWithAllRequiredFields()
    {
        return Prop.ForAll(GenEventEnvelope().ToArbitrary(), envelope =>
        {
            var json = JsonSerializer.Serialize(envelope, SerializerOptions);
            var deserialized = JsonSerializer.Deserialize<EventEnvelope>(json, SerializerOptions);

            var hasNonEmptyEventId = deserialized!.EventId != Guid.Empty;
            var hasNonEmptyEventType = !string.IsNullOrEmpty(deserialized.EventType);
            var hasNonEmptyCorrelationId = !string.IsNullOrEmpty(deserialized.CorrelationId);
            var hasUtcTimestamp = deserialized.Timestamp.Kind == DateTimeKind.Utc;
            var hasNonNullPayload = deserialized.Payload is not null;

            return hasNonEmptyEventId
                .Label("EventId must be non-empty GUID")
                .And(hasNonEmptyEventType
                    .Label("EventType must be non-empty"))
                .And(hasNonEmptyCorrelationId
                    .Label("CorrelationId must be non-empty"))
                .And(hasUtcTimestamp
                    .Label("Timestamp must be UTC"))
                .And(hasNonNullPayload
                    .Label("Payload must not be null"));
        });
    }

    /// <summary>
    /// **Validates: Requirements 3.4, 3.7**
    ///
    /// Property 2 (equivalence): For any EventEnvelope, serializing to JSON and
    /// deserializing back SHALL produce an object with equivalent field values
    /// to the original (EventId, EventType, CorrelationId, Timestamp match exactly).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SerializedEnvelope_RoundTripPreservesFieldValues()
    {
        return Prop.ForAll(GenEventEnvelope().ToArbitrary(), envelope =>
        {
            var json = JsonSerializer.Serialize(envelope, SerializerOptions);
            var deserialized = JsonSerializer.Deserialize<EventEnvelope>(json, SerializerOptions);

            var eventIdMatches = deserialized!.EventId == envelope.EventId;
            var eventTypeMatches = deserialized.EventType == envelope.EventType;
            var correlationIdMatches = deserialized.CorrelationId == envelope.CorrelationId;
            var timestampMatches = deserialized.Timestamp == envelope.Timestamp;

            return eventIdMatches
                .Label($"EventId mismatch: expected {envelope.EventId}, got {deserialized.EventId}")
                .And(eventTypeMatches
                    .Label($"EventType mismatch: expected '{envelope.EventType}', got '{deserialized.EventType}'"))
                .And(correlationIdMatches
                    .Label($"CorrelationId mismatch: expected '{envelope.CorrelationId}', got '{deserialized.CorrelationId}'"))
                .And(timestampMatches
                    .Label($"Timestamp mismatch: expected {envelope.Timestamp:O}, got {deserialized.Timestamp:O}"));
        });
    }
}
