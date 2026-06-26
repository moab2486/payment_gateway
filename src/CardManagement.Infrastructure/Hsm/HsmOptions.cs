namespace CardManagement.Infrastructure.Hsm;

/// <summary>
/// Configuration options for the HSM (Hardware Security Module) connection.
/// Bound from environment variables with the "HSM" prefix.
/// </summary>
public sealed class HsmOptions
{
    public const string SectionName = "HSM";

    /// <summary>
    /// Path to the PKCS#11 library (e.g., /usr/lib/softhsm/libsofthsm2.so).
    /// Environment variable: HSM__ENDPOINT
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// The slot identifier on the HSM.
    /// Environment variable: HSM__SLOT_ID
    /// </summary>
    public ulong SlotId { get; set; }

    /// <summary>
    /// The PIN used for authenticating to the HSM slot.
    /// Environment variable: HSM__PIN
    /// </summary>
    public string Pin { get; set; } = string.Empty;

    /// <summary>
    /// Authentication timeout in seconds. Default is 5 seconds.
    /// Environment variable: HSM__AUTH_TIMEOUT_SECONDS
    /// </summary>
    public int AuthTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Operation timeout in seconds. Default is 10 seconds.
    /// Environment variable: HSM__OPERATION_TIMEOUT_SECONDS
    /// </summary>
    public int OperationTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// The label of the CVV key stored in the HSM (used for CVV2 computation).
    /// Environment variable: HSM__CVV_KEY_LABEL
    /// </summary>
    public string CvvKeyLabel { get; set; } = "cvv-key";
}
