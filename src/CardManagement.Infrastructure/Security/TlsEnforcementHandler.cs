using System.Net.Security;
using System.Security.Authentication;

namespace CardManagement.Infrastructure.Security;

/// <summary>
/// Provides TLS 1.2+ enforcement for outbound cardholder data transmission.
/// Configures HttpClientHandler and SslStream settings to reject connections below TLS 1.2.
/// </summary>
public static class TlsEnforcementHandler
{
    /// <summary>
    /// Creates an HttpClientHandler configured to enforce TLS 1.2 or higher for all connections.
    /// Use this in HttpClient factories that handle cardholder data.
    /// </summary>
    public static HttpClientHandler CreateSecureHandler()
    {
        return new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ClientCertificateOptions = ClientCertificateOption.Automatic
        };
    }

    /// <summary>
    /// Creates an HttpClient with TLS 1.2+ enforcement for PCI-compliant outbound communication.
    /// </summary>
    public static HttpClient CreateSecureHttpClient()
    {
        var handler = CreateSecureHandler();
        return new HttpClient(handler);
    }

    /// <summary>
    /// Returns the SslClientAuthenticationOptions for TCP connections that transmit cardholder data.
    /// Enforces TLS 1.2 or higher.
    /// </summary>
    public static SslClientAuthenticationOptions GetSecureSslOptions(string targetHost)
    {
        return new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.Online
        };
    }

    /// <summary>
    /// Validates that the provided SslProtocols meet minimum TLS 1.2 requirements.
    /// Returns true if compliant, false otherwise.
    /// </summary>
    public static bool IsProtocolCompliant(SslProtocols protocol)
    {
        // Only TLS 1.2 and TLS 1.3 are PCI-compliant
        var allowedProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        return (protocol & allowedProtocols) != 0 &&
               (protocol & ~allowedProtocols) == 0;
    }
}
