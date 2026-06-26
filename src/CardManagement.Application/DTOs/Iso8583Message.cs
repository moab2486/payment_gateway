namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents a structured ISO 8583 message with MTI, bitmap-driven fields, and raw bytes.
/// Used as the application-level representation of parsed ISO 8583 binary messages.
/// </summary>
public record Iso8583Message
{
    /// <summary>
    /// Message Type Indicator (e.g., "0100" for authorization request, "0420" for reversal).
    /// </summary>
    public string Mti { get; init; } = string.Empty;

    /// <summary>
    /// Bitmap-driven collection of field numbers to their string values.
    /// Field numbers follow the ISO 8583 specification (1-128).
    /// </summary>
    public IReadOnlyDictionary<int, string> Fields { get; init; } = new Dictionary<int, string>();

    /// <summary>
    /// The raw binary representation of the message.
    /// Populated when parsing inbound messages; null for newly constructed messages.
    /// </summary>
    public byte[]? RawBytes { get; init; }
}
