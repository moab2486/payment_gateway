using CardManagement.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for ConfigurationService verifying that all required configuration keys
/// are validated for presence and correct format at application startup.
/// </summary>
public class ConfigurationServiceTests
{
    private static readonly ILogger<ConfigurationService> Logger = NullLogger<ConfigurationService>.Instance;

    private static readonly Dictionary<string, string?> ValidConfiguration = new()
    {
        ["DATABASE__CONNECTION_STRING"] = "Host=localhost;Database=cardmgmt;Username=admin;Password=secret",
        ["TCP__LISTENER_PORT"] = "8583",
        ["TCP__MAX_CONNECTIONS"] = "100",
        ["TCP__READ_TIMEOUT_SECONDS"] = "30",
        ["HSM__ENDPOINT"] = "https://hsm.example.com",
        ["HSM__SLOT_ID"] = "slot-001",
        ["HSM__AUTH_TIMEOUT_SECONDS"] = "5",
        ["HSM__OPERATION_TIMEOUT_SECONDS"] = "10",
        ["PROCESSOR__INTERSWITCH_ENDPOINT"] = "https://interswitch.example.com:8443",
        ["PROCESSOR__CARDFI_ENDPOINT"] = "cardfi.example.com:9443",
        ["CARD__BIN_RANGES"] = "[{\"prefix\":\"506199\",\"scheme\":\"Verve\",\"panLength\":16},{\"prefix\":\"4\",\"scheme\":\"Visa\",\"panLength\":16}]",
        ["CARD__VALIDITY_MONTHS"] = "36",
        ["LEDGER__LOCK_TIMEOUT_SECONDS"] = "5"
    };

    private ConfigurationService CreateService(Dictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        return new ConfigurationService(configuration, Logger);
    }

    [Fact]
    public void ValidateConfiguration_AllValid_ReturnsSuccess()
    {
        var service = CreateService(ValidConfiguration);

        var result = service.ValidateConfiguration();

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("DATABASE__CONNECTION_STRING")]
    [InlineData("TCP__LISTENER_PORT")]
    [InlineData("TCP__MAX_CONNECTIONS")]
    [InlineData("TCP__READ_TIMEOUT_SECONDS")]
    [InlineData("HSM__ENDPOINT")]
    [InlineData("HSM__SLOT_ID")]
    [InlineData("HSM__AUTH_TIMEOUT_SECONDS")]
    [InlineData("HSM__OPERATION_TIMEOUT_SECONDS")]
    [InlineData("PROCESSOR__INTERSWITCH_ENDPOINT")]
    [InlineData("PROCESSOR__CARDFI_ENDPOINT")]
    [InlineData("CARD__BIN_RANGES")]
    [InlineData("CARD__VALIDITY_MONTHS")]
    [InlineData("LEDGER__LOCK_TIMEOUT_SECONDS")]
    public void ValidateConfiguration_MissingKey_ReturnsFailure(string missingKey)
    {
        var config = new Dictionary<string, string?>(ValidConfiguration);
        config.Remove(missingKey);

        var service = CreateService(config);

        var result = service.ValidateConfiguration();

        Assert.False(result.IsSuccess);
        Assert.Contains(missingKey, result.ErrorMessage);
    }

    [Theory]
    [InlineData("TCP__LISTENER_PORT", "abc")]
    [InlineData("TCP__LISTENER_PORT", "-1")]
    [InlineData("TCP__LISTENER_PORT", "99999")]
    [InlineData("TCP__MAX_CONNECTIONS", "0")]
    [InlineData("TCP__MAX_CONNECTIONS", "not_a_number")]
    [InlineData("TCP__READ_TIMEOUT_SECONDS", "")]
    [InlineData("HSM__AUTH_TIMEOUT_SECONDS", "zero")]
    [InlineData("HSM__OPERATION_TIMEOUT_SECONDS", "0")]
    [InlineData("CARD__VALIDITY_MONTHS", "0")]
    [InlineData("CARD__VALIDITY_MONTHS", "61")]
    [InlineData("LEDGER__LOCK_TIMEOUT_SECONDS", "-5")]
    public void ValidateConfiguration_MalformedIntegerValue_ReturnsFailure(string key, string badValue)
    {
        var config = new Dictionary<string, string?>(ValidConfiguration)
        {
            [key] = badValue
        };

        var service = CreateService(config);

        var result = service.ValidateConfiguration();

        Assert.False(result.IsSuccess);
        Assert.Contains(key, result.ErrorMessage);
    }

    [Theory]
    [InlineData("not_json_at_all")]
    [InlineData("[]")]
    [InlineData("[{}]")]
    [InlineData("[{\"prefix\":\"506199\",\"scheme\":\"InvalidScheme\",\"panLength\":16}]")]
    [InlineData("[{\"prefix\":\"506199\",\"scheme\":\"Verve\",\"panLength\":15}]")]
    [InlineData("[{\"prefix\":\"506199\",\"scheme\":\"Verve\",\"panLength\":20}]")]
    public void ValidateConfiguration_InvalidBinRanges_ReturnsFailure(string badBinRanges)
    {
        var config = new Dictionary<string, string?>(ValidConfiguration)
        {
            ["CARD__BIN_RANGES"] = badBinRanges
        };

        var service = CreateService(config);

        var result = service.ValidateConfiguration();

        Assert.False(result.IsSuccess);
        Assert.Contains("CARD__BIN_RANGES", result.ErrorMessage);
    }

    [Theory]
    [InlineData("not_a_valid_endpoint")]
    [InlineData("://missing-scheme")]
    public void ValidateConfiguration_InvalidProcessorEndpoint_ReturnsFailure(string badEndpoint)
    {
        var config = new Dictionary<string, string?>(ValidConfiguration)
        {
            ["PROCESSOR__INTERSWITCH_ENDPOINT"] = badEndpoint
        };

        var service = CreateService(config);

        var result = service.ValidateConfiguration();

        Assert.False(result.IsSuccess);
        Assert.Contains("PROCESSOR__INTERSWITCH_ENDPOINT", result.ErrorMessage);
    }

    [Fact]
    public void ValidateConfiguration_MultipleInvalidKeys_ReportsAll()
    {
        var config = new Dictionary<string, string?>(ValidConfiguration);
        config.Remove("DATABASE__CONNECTION_STRING");
        config["TCP__LISTENER_PORT"] = "invalid";
        config["CARD__BIN_RANGES"] = "bad_json";

        var service = CreateService(config);

        var result = service.ValidateConfiguration();

        Assert.False(result.IsSuccess);
        Assert.Contains("DATABASE__CONNECTION_STRING", result.ErrorMessage);
        Assert.Contains("TCP__LISTENER_PORT", result.ErrorMessage);
        Assert.Contains("CARD__BIN_RANGES", result.ErrorMessage);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://192.168.1.1:8080")]
    [InlineData("host.example.com:8443")]
    [InlineData("192.168.1.1:9090")]
    public void ValidateConfiguration_ValidEndpointFormats_ReturnsSuccess(string endpoint)
    {
        var config = new Dictionary<string, string?>(ValidConfiguration)
        {
            ["PROCESSOR__INTERSWITCH_ENDPOINT"] = endpoint,
            ["PROCESSOR__CARDFI_ENDPOINT"] = endpoint
        };

        var service = CreateService(config);

        var result = service.ValidateConfiguration();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateConfiguration_ValidBinRangesMultipleEntries_ReturnsSuccess()
    {
        var binRanges = "[{\"prefix\":\"506199\",\"scheme\":\"Verve\",\"panLength\":16}," +
                        "{\"prefix\":\"4\",\"scheme\":\"Visa\",\"panLength\":16}," +
                        "{\"prefix\":\"5\",\"scheme\":\"Mastercard\",\"panLength\":16}]";

        var config = new Dictionary<string, string?>(ValidConfiguration)
        {
            ["CARD__BIN_RANGES"] = binRanges
        };

        var service = CreateService(config);

        var result = service.ValidateConfiguration();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateConfiguration_ErrorCode_IsConfigurationInvalid()
    {
        var config = new Dictionary<string, string?>(ValidConfiguration);
        config.Remove("DATABASE__CONNECTION_STRING");

        var service = CreateService(config);

        var result = service.ValidateConfiguration();

        Assert.Equal("CONFIGURATION_INVALID", result.ErrorCode);
    }
}
