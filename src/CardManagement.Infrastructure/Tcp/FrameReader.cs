using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;

namespace CardManagement.Infrastructure.Tcp;

/// <summary>
/// Reads length-prefixed frames from a PipeReader using the 2-byte big-endian
/// length prefix framing protocol used by ISO 8583 over TCP.
/// 
/// Frame format:
///   [2 bytes: message length (big-endian)] [N bytes: message payload]
/// 
/// The length prefix indicates the number of payload bytes that follow.
/// </summary>
public static class FrameReader
{
    /// <summary>
    /// The size of the length prefix header in bytes.
    /// </summary>
    public const int LengthPrefixSize = 2;

    /// <summary>
    /// Attempts to read a complete length-prefixed frame from the given PipeReader.
    /// Returns the payload bytes (excluding the 2-byte length prefix) once a full frame is available.
    /// </summary>
    /// <param name="reader">The PipeReader to read from.</param>
    /// <param name="maxFrameSize">Maximum allowed frame payload size in bytes.</param>
    /// <param name="readTimeout">Maximum time to wait for a complete frame.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="FrameReadResult"/> indicating the outcome:
    /// - Success with payload data
    /// - OversizedFrame if the declared length exceeds maxFrameSize
    /// - Timeout if readTimeout elapsed before a complete frame arrived
    /// - ConnectionClosed if the remote endpoint disconnected
    /// </returns>
    public static async Task<FrameReadResult> ReadFrameAsync(
        PipeReader reader,
        int maxFrameSize,
        TimeSpan readTimeout,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(readTimeout);

        try
        {
            while (true)
            {
                ReadResult readResult = await reader.ReadAsync(timeoutCts.Token);
                ReadOnlySequence<byte> buffer = readResult.Buffer;

                if (TryParseFrame(buffer, maxFrameSize, out var payload, out var consumed, out var frameStatus))
                {
                    reader.AdvanceTo(consumed);

                    if (frameStatus == FrameStatus.OversizedFrame)
                    {
                        return FrameReadResult.Oversized();
                    }

                    return FrameReadResult.Success(payload!);
                }

                // If the reader has completed (connection closed) and we still don't have a full frame
                if (readResult.IsCompleted)
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                    return FrameReadResult.Closed();
                }

                // Tell the PipeReader we've examined up to the end but consumed nothing yet
                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The timeout CTS fired, not the external cancellation
            return FrameReadResult.TimedOut();
        }
    }

    /// <summary>
    /// Attempts to parse a single length-prefixed frame from the buffer.
    /// This is a pure parsing method suitable for property-based testing.
    /// </summary>
    /// <param name="buffer">The buffer to parse from.</param>
    /// <param name="maxFrameSize">Maximum allowed frame payload size.</param>
    /// <param name="payload">The extracted payload bytes if successful.</param>
    /// <param name="consumed">The position up to which data was consumed.</param>
    /// <param name="status">The parse status.</param>
    /// <returns>True if a frame was parsed (successfully or as oversized); false if more data is needed.</returns>
    public static bool TryParseFrame(
        ReadOnlySequence<byte> buffer,
        int maxFrameSize,
        out byte[]? payload,
        out SequencePosition consumed,
        out FrameStatus status)
    {
        payload = null;
        consumed = buffer.Start;
        status = FrameStatus.Incomplete;

        // Need at least 2 bytes for the length prefix
        if (buffer.Length < LengthPrefixSize)
        {
            return false;
        }

        // Read the 2-byte big-endian length prefix
        ushort frameLength = ReadLengthPrefix(buffer);

        // Check if the declared frame size exceeds maximum
        if (frameLength > maxFrameSize)
        {
            status = FrameStatus.OversizedFrame;
            consumed = buffer.GetPosition(LengthPrefixSize);
            return true;
        }

        // Check if we have the complete frame (prefix + payload)
        long totalFrameSize = LengthPrefixSize + (long)frameLength;
        if (buffer.Length < totalFrameSize)
        {
            return false;
        }

        // Extract the payload
        ReadOnlySequence<byte> payloadSequence = buffer.Slice(LengthPrefixSize, frameLength);
        payload = payloadSequence.ToArray();
        consumed = buffer.GetPosition(totalFrameSize);
        status = FrameStatus.Complete;
        return true;
    }

    /// <summary>
    /// Writes a length-prefixed frame to the specified PipeWriter.
    /// Prepends a 2-byte big-endian length prefix followed by the payload.
    /// </summary>
    /// <param name="writer">The PipeWriter to write to.</param>
    /// <param name="payload">The payload bytes to frame and write.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task WriteFrameAsync(PipeWriter writer, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        // Write 2-byte big-endian length prefix
        var lengthPrefix = writer.GetMemory(LengthPrefixSize);
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix.Span, (ushort)payload.Length);
        writer.Advance(LengthPrefixSize);

        // Write payload
        var payloadBuffer = writer.GetMemory(payload.Length);
        payload.Span.CopyTo(payloadBuffer.Span);
        writer.Advance(payload.Length);

        await writer.FlushAsync(ct);
    }

    /// <summary>
    /// Encodes a payload into a length-prefixed frame as a byte array.
    /// Useful for testing and simple framing scenarios.
    /// </summary>
    /// <param name="payload">The payload to frame.</param>
    /// <returns>A byte array containing [2-byte big-endian length][payload].</returns>
    public static byte[] EncodeFrame(ReadOnlySpan<byte> payload)
    {
        var frame = new byte[LengthPrefixSize + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, LengthPrefixSize), (ushort)payload.Length);
        payload.CopyTo(frame.AsSpan(LengthPrefixSize));
        return frame;
    }

    private static ushort ReadLengthPrefix(ReadOnlySequence<byte> buffer)
    {
        Span<byte> lengthBytes = stackalloc byte[LengthPrefixSize];

        if (buffer.FirstSpan.Length >= LengthPrefixSize)
        {
            // Fast path: length prefix is in the first segment
            return BinaryPrimitives.ReadUInt16BigEndian(buffer.FirstSpan);
        }

        // Slow path: length prefix spans multiple segments
        buffer.Slice(0, LengthPrefixSize).CopyTo(lengthBytes);
        return BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
    }
}

/// <summary>
/// Represents the result of a frame read operation.
/// </summary>
public class FrameReadResult
{
    public FrameReadStatus Status { get; }
    public byte[]? Payload { get; }

    private FrameReadResult(FrameReadStatus status, byte[]? payload = null)
    {
        Status = status;
        Payload = payload;
    }

    public static FrameReadResult Success(byte[] payload) => new(FrameReadStatus.Success, payload);
    public static FrameReadResult Oversized() => new(FrameReadStatus.OversizedFrame);
    public static FrameReadResult TimedOut() => new(FrameReadStatus.Timeout);
    public static FrameReadResult Closed() => new(FrameReadStatus.ConnectionClosed);
}

/// <summary>
/// Status codes for frame read operations.
/// </summary>
public enum FrameReadStatus
{
    Success,
    OversizedFrame,
    Timeout,
    ConnectionClosed
}

/// <summary>
/// Internal status for the frame parsing step.
/// </summary>
public enum FrameStatus
{
    /// <summary>More data is needed to complete the frame.</summary>
    Incomplete,
    /// <summary>A complete frame was successfully parsed.</summary>
    Complete,
    /// <summary>The declared frame length exceeds the maximum allowed size.</summary>
    OversizedFrame
}
