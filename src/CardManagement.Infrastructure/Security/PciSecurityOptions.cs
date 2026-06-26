namespace CardManagement.Infrastructure.Security;

/// <summary>
/// Configuration options for the PCI security boundary.
/// </summary>
public class PciSecurityOptions
{
    public const string SectionName = "PciSecurity";

    /// <summary>
    /// Base64-encoded 256-bit AES encryption key for cardholder data at rest.
    /// </summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// List of service names authorized to access raw cardholder data.
    /// </summary>
    public List<string> AllowedServices { get; set; } = new();

    /// <summary>
    /// Minimum TLS version enforced for outbound cardholder data transmission.
    /// </summary>
    public string MinimumTlsVersion { get; set; } = "1.2";
}
