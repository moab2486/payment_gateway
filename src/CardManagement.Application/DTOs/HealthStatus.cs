namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents the health status of a channel adapter.
/// </summary>
public record HealthStatus(
    bool IsHealthy,
    string? Details);
