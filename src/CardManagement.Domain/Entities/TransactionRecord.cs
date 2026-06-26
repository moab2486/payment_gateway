using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;

namespace CardManagement.Domain.Entities;

/// <summary>
/// Entity representing a processed card transaction.
/// Tracks the full lifecycle from receipt through pipeline processing to completion.
/// </summary>
public class TransactionRecord
{
    public Guid Id { get; private set; }
    public string SystemTraceAuditNumber { get; private set; } = string.Empty;
    public string MessageType { get; private set; } = string.Empty;
    public string? ResponseCode { get; private set; }
    public Guid CardId { get; private set; }
    public long Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public ProcessorType ProcessorType { get; private set; }
    public TransactionStatus Status { get; private set; }
    public string? PipelineStepReached { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private TransactionRecord() { }

    public static TransactionRecord Create(
        string systemTraceAuditNumber,
        string messageType,
        Guid cardId,
        long amount,
        string currency,
        ProcessorType processorType)
    {
        if (string.IsNullOrWhiteSpace(systemTraceAuditNumber))
            throw new ArgumentException("System trace audit number is required.", nameof(systemTraceAuditNumber));

        if (string.IsNullOrWhiteSpace(messageType))
            throw new ArgumentException("Message type is required.", nameof(messageType));

        if (cardId == Guid.Empty)
            throw new ArgumentException("Card ID is required.", nameof(cardId));

        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));

        return new TransactionRecord
        {
            Id = Guid.NewGuid(),
            SystemTraceAuditNumber = systemTraceAuditNumber,
            MessageType = messageType,
            CardId = cardId,
            Amount = amount,
            Currency = currency,
            ProcessorType = processorType,
            Status = TransactionStatus.Pending,
            CreatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow)
        };
    }

    public void Approve(string responseCode, string pipelineStepReached)
    {
        ResponseCode = responseCode;
        PipelineStepReached = pipelineStepReached;
        Status = TransactionStatus.Approved;
    }

    public void Decline(string responseCode, string pipelineStepReached)
    {
        ResponseCode = responseCode;
        PipelineStepReached = pipelineStepReached;
        Status = TransactionStatus.Declined;
    }

    public void Reverse(string responseCode)
    {
        ResponseCode = responseCode;
        Status = TransactionStatus.Reversed;
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
