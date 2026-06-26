using System.Security.Cryptography;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Verifies webhook destination URLs using a challenge-response mechanism.
/// Sends GET {url}?challenge={token} and expects HTTP 200 with the token in the response body.
/// </summary>
public sealed class UrlVerificationService : IUrlVerificationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UrlVerificationService> _logger;

    public UrlVerificationService(HttpClient httpClient, ILogger<UrlVerificationService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> VerifyChallengeAsync(string destinationUrl, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationUrl);

        var challengeToken = GenerateChallengeToken();
        var separator = destinationUrl.Contains('?') ? "&" : "?";
        var challengeUrl = $"{destinationUrl}{separator}challenge={Uri.EscapeDataString(challengeToken)}";

        try
        {
            using var response = await _httpClient.GetAsync(challengeUrl, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "URL verification failed for {Url}: received HTTP {StatusCode}",
                    destinationUrl, (int)response.StatusCode);
                return false;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var isValid = responseBody.Trim() == challengeToken;

            if (!isValid)
            {
                _logger.LogWarning(
                    "URL verification failed for {Url}: challenge token mismatch. Expected '{Expected}', got '{Actual}'",
                    destinationUrl, challengeToken, responseBody.Trim());
            }

            return isValid;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "URL verification failed for {Url}: {Message}", destinationUrl, ex.Message);
            return false;
        }
    }

    private static string GenerateChallengeToken()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(tokenBytes).ToLowerInvariant();
    }
}
