using CardManagement.Application.DTOs;

namespace CardManagement.Application.Ports;

/// <summary>
/// Port interface for validating runtime configuration at application startup.
/// Checks that all required configuration keys (connection strings, endpoints,
/// timeouts, BIN ranges, etc.) are present and well-formed.
/// </summary>
public interface IConfigurationValidator
{
    /// <summary>
    /// Validates all required configuration values are present and correctly formatted.
    /// If any required value is missing or malformed, returns a failure result
    /// identifying each invalid configuration key.
    /// </summary>
    /// <returns>
    /// A success result if all configuration is valid, or a failure result
    /// listing the missing/invalid configuration keys.
    /// </returns>
    Result ValidateConfiguration();
}
