namespace CardManagement.Application.PlatformServices.AdminConsole.DTOs;

/// <summary>
/// Result of executing an approved admin command.
/// </summary>
public record CommandExecutionResult(
    Guid CommandId,
    bool Success,
    string? ResultMessage,
    DateTime ExecutedAtUtc);
