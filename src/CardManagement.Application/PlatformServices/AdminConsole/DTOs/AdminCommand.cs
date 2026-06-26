namespace CardManagement.Application.PlatformServices.AdminConsole.DTOs;

/// <summary>
/// Represents a command submission request from an admin user.
/// </summary>
public record AdminCommand(
    string CommandType,
    string SerializedParameters,
    bool IsSensitive);
