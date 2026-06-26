using System.Text.Json;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.Configuration;

/// <summary>
/// Validates all required runtime configuration at application startup.
/// Reads values from environment variables/files via IConfiguration and
/// ensures each required key is present and well-formed.
/// </summary>
public sealed class ConfigurationService : IConfigurationValidator
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationService> _logger;

    public ConfigurationService(IConfiguration configuration, ILogger<ConfigurationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Result ValidateConfiguration()
    {
        var errors = new List<string>();

        ValidateRequiredString("DATABASE__CONNECTION_STRING", errors);
        ValidatePositiveInteger("TCP__LISTENER_PORT", errors, min: 1, max: 65535);
        ValidatePositiveInteger("TCP__MAX_CONNECTIONS", errors, min: 1);
        ValidatePositiveInteger("TCP__READ_TIMEOUT_SECONDS", errors, min: 1);
        ValidateRequiredString("HSM__ENDPOINT", errors);
        ValidateRequiredString("HSM__SLOT_ID", errors);
        ValidatePositiveInteger("HSM__AUTH_TIMEOUT_SECONDS", errors, min: 1);
        ValidatePositiveInteger("HSM__OPERATION_TIMEOUT_SECONDS", errors, min: 1);
        ValidateEndpoint("PROCESSOR__INTERSWITCH_ENDPOINT", errors);
        ValidateEndpoint("PROCESSOR__CARDFI_ENDPOINT", errors);
        ValidateBinRangesJson("CARD__BIN_RANGES", errors);
        ValidatePositiveInteger("CARD__VALIDITY_MONTHS", errors, min: 1, max: 60);
        ValidatePositiveInteger("LEDGER__LOCK_TIMEOUT_SECONDS", errors, min: 1);

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                _logger.LogError("Configuration validation failed: {Error}", error);
            }

            var message = $"Configuration validation failed. Invalid keys: {string.Join("; ", errors)}";
            return Result.Failure(message, "CONFIGURATION_INVALID");
        }

        return Result.Success();
    }

    private void ValidateRequiredString(string key, List<string> errors)
    {
        var value = _configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key}: missing or empty");
        }
    }

    private void ValidatePositiveInteger(string key, List<string> errors, int min = 1, int? max = null)
    {
        var value = _configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key}: missing or empty");
            return;
        }

        if (!int.TryParse(value, out var intValue))
        {
            errors.Add($"{key}: value '{value}' is not a valid integer");
            return;
        }

        if (intValue < min)
        {
            errors.Add($"{key}: value {intValue} is below minimum {min}");
            return;
        }

        if (max.HasValue && intValue > max.Value)
        {
            errors.Add($"{key}: value {intValue} exceeds maximum {max.Value}");
        }
    }

    private void ValidateEndpoint(string key, List<string> errors)
    {
        var value = _configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key}: missing or empty");
            return;
        }

        // Endpoint should be a valid URI or host:port format
        if (Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return;
        }

        // Try host:port format
        var parts = value.Split(':');
        if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && int.TryParse(parts[1], out var port) && port > 0 && port <= 65535)
        {
            return;
        }

        errors.Add($"{key}: value '{value}' is not a valid endpoint (expected URI or host:port)");
    }

    private void ValidateBinRangesJson(string key, List<string> errors)
    {
        var value = _configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key}: missing or empty");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var ranges = JsonSerializer.Deserialize<List<BinRangeConfigDto>>(value, options);

            if (ranges is null || ranges.Count == 0)
            {
                errors.Add($"{key}: JSON is empty or not a valid array of BIN ranges");
                return;
            }

            for (int i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (string.IsNullOrWhiteSpace(range.Prefix))
                {
                    errors.Add($"{key}[{i}]: missing 'prefix' property");
                }

                if (string.IsNullOrWhiteSpace(range.Scheme))
                {
                    errors.Add($"{key}[{i}]: missing 'scheme' property");
                }
                else if (!IsValidScheme(range.Scheme))
                {
                    errors.Add($"{key}[{i}]: invalid scheme '{range.Scheme}' (expected Verve, Visa, or Mastercard)");
                }

                if (range.PanLength < 16 || range.PanLength > 19)
                {
                    errors.Add($"{key}[{i}]: panLength {range.PanLength} is out of valid range (16-19)");
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"{key}: invalid JSON format - {ex.Message}");
        }
    }

    private static bool IsValidScheme(string scheme)
    {
        return string.Equals(scheme, "Verve", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scheme, "Visa", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scheme, "Mastercard", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BinRangeConfigDto
    {
        public string? Prefix { get; set; }
        public string? Scheme { get; set; }
        public int PanLength { get; set; }
    }
}
