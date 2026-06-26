namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents the result of processing a payment through a channel adapter.
/// </summary>
public record ChannelResult(
    bool Success,
    string? ProcessorReference,
    string? ErrorCode,
    string? ErrorMessage);
