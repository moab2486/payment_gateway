using CardManagement.Application.DTOs;

namespace CardManagement.Application.Ports;

/// <summary>
/// Port interface for parsing and constructing ISO 8583 binary messages.
/// Implementations use an open-source library (e.g., NetCore8583) for bitmap-driven
/// field encoding and decoding.
/// </summary>
public interface IIso8583Gateway
{
    /// <summary>
    /// Parses inbound ISO 8583 binary message bytes into a structured message object.
    /// Returns a failure result with response code "96" (system malfunction) for malformed messages.
    /// </summary>
    /// <param name="rawMessage">The raw binary ISO 8583 message bytes.</param>
    /// <returns>A result containing the parsed message or an error.</returns>
    Result<Iso8583Message> Parse(ReadOnlySpan<byte> rawMessage);

    /// <summary>
    /// Constructs an outbound ISO 8583 binary message from a structured message object.
    /// </summary>
    /// <param name="message">The structured ISO 8583 message to encode.</param>
    /// <returns>A result containing the encoded binary message or an error.</returns>
    Result<byte[]> Construct(Iso8583Message message);

    /// <summary>
    /// Validates whether a message length is within the allowed frame size.
    /// Messages exceeding 9999 bytes are rejected.
    /// </summary>
    /// <param name="messageLength">The length of the message in bytes.</param>
    /// <returns>True if the frame size is valid; false if it exceeds the maximum.</returns>
    bool IsValidFrameSize(int messageLength);
}
