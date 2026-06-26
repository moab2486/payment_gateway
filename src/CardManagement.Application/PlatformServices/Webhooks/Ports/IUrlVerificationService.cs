namespace CardManagement.Application.PlatformServices.Webhooks.Ports;

/// <summary>
/// Port for verifying webhook destination URLs via a challenge-response mechanism.
/// Sends a GET request to the URL with a challenge token query parameter and expects
/// the endpoint to return HTTP 200 with the token in the response body.
/// </summary>
public interface IUrlVerificationService
{
    /// <summary>
    /// Verifies that the destination URL is reachable and responds correctly to a challenge.
    /// </summary>
    /// <param name="destinationUrl">The webhook destination URL to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the URL responded correctly to the challenge; otherwise false.</returns>
    Task<bool> VerifyChallengeAsync(string destinationUrl, CancellationToken ct);
}
