namespace CardManagement.Application.PlatformServices.AdminConsole.DTOs;

/// <summary>
/// Metadata about a read model's projection state, including staleness information.
/// </summary>
public record ReadModelMetadata(
    string ReadModelName,
    DateTime LastProjectedAtUtc,
    bool IsStale,
    TimeSpan? StaleDuration);
