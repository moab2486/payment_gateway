namespace CardManagement.Application.PlatformServices.Webhooks.Ports;

/// <summary>
/// Port for computing and verifying HMAC-SHA256 signatures on webhook payloads.
/// </summary>
public interface IHmacSigner
{
    string ComputeSignature(string payload, string secret);
    bool VerifySignature(string payload, string secret, string signature);
}
