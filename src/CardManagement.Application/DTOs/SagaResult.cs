namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents the outcome of a saga execution.
/// </summary>
public record SagaResult(
    string TransactionReference,
    bool Success,
    string? FailedStep,
    string? ErrorMessage);
