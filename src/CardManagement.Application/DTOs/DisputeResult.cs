using CardManagement.Domain.Enums;

namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents the result of a dispute operation (raise or resolve).
/// </summary>
public record DisputeResult(
    Guid Id,
    DisputeStatus Status,
    bool Success,
    string? ErrorMessage);
