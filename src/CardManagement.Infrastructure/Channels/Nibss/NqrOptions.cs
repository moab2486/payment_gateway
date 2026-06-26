namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Configuration options for the NIBSS Quick Response (NQR) adapter.
/// Bound from the "Nqr" configuration section.
/// </summary>
public class NqrOptions
{
    public const string SectionName = "Nqr";

    /// <summary>
    /// Base URL for the NIBSS NQR API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authenticating with NIBSS NQR.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Expiry time in minutes for dynamic QR codes.
    /// Dynamic QR codes become invalid after this period.
    /// </summary>
    public int DynamicQrExpiryMinutes { get; set; } = 15;
}
