using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using CardManagement.Infrastructure.Tcp;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for the FrameReader utility class that handles 2-byte big-endian
/// length prefix framing protocol.
/// </summary>
public class FrameReaderTests
{
    [Fact]
    public void TryParseFrame_WithCompleteFrame_ReturnsPayload()
    {
        // Arrange
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var frame = FrameReader.EncodeFrame(payload);
        var buffer = new ReadOnlySequence<byte>(frame);

        // Act
        var result = FrameReader.TryParseFrame(buffer, 9999, out var parsedPayload, out var consumed, out var status);

        // Assert
        Assert.True(result);
        Assert.Equal(FrameStatus.Complete, status);
        Assert.NotNull(parsedPayload);
        Assert.Equal(payload, parsedPayload);
    }

    [Fact]
    public void TryParseFrame_WithIncompleteHeader_ReturnsFalse()
    {
        // Arrange - only 1 byte, need 2 for header
        var buffer = new ReadOnlySequence<byte>(new byte[] { 0x00 });

        // Act
        var result = FrameReader.TryParseFrame(buffer, 9999, out var payload, out _, out var status);

        // Assert
        Assert.False(result);
        Assert.Equal(FrameStatus.Incomplete, status);
        Assert.Null(payload);
    }

    [Fact]
    public void TryParseFrame_WithIncompletePayload_ReturnsFalse()
    {
        // Arrange - header says 5 bytes, but only 3 are present
        var buffer = new ReadOnlySequence<byte>(new byte[] { 0x00, 0x05, 0x01, 0x02, 0x03 });

        // Act
        var result = FrameReader.TryParseFrame(buffer, 9999, out var payload, out _, out var status);

        // Assert
        Assert.False(result);
        Assert.Equal(FrameStatus.Incomplete, status);
        Assert.Null(payload);
    }

    [Fact]
    public void TryParseFrame_WithOversizedFrame_ReturnsOversized()
    {
        // Arrange - header declares 100 bytes, but max is 50
        var header = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(header, 100);
        var buffer = new ReadOnlySequence<byte>(header);

        // Act
        var result = FrameReader.TryParseFrame(buffer, 50, out var payload, out _, out var status);

        // Assert
        Assert.True(result);
        Assert.Equal(FrameStatus.OversizedFrame, status);
        Assert.Null(payload);
    }

    [Fact]
    public void TryParseFrame_WithEmptyPayload_ReturnsEmptyArray()
    {
        // Arrange - zero-length payload
        var frame = new byte[] { 0x00, 0x00 };
        var buffer = new ReadOnlySequence<byte>(frame);

        // Act
        var result = FrameReader.TryParseFrame(buffer, 9999, out var payload, out _, out var status);

        // Assert
        Assert.True(result);
        Assert.Equal(FrameStatus.Complete, status);
        Assert.NotNull(payload);
        Assert.Empty(payload);
    }

    [Fact]
    public void TryParseFrame_WithMaxFrameSize_ReturnsPayload()
    {
        // Arrange - exactly at the max frame size boundary
        var payloadData = new byte[9999];
        Array.Fill(payloadData, (byte)0xAA);
        var frame = FrameReader.EncodeFrame(payloadData);
        var buffer = new ReadOnlySequence<byte>(frame);

        // Act
        var result = FrameReader.TryParseFrame(buffer, 9999, out var payload, out _, out var status);

        // Assert
        Assert.True(result);
        Assert.Equal(FrameStatus.Complete, status);
        Assert.Equal(payloadData, payload);
    }

    [Fact]
    public void TryParseFrame_WithFrameSizeExceedingMaxByOne_ReturnsOversized()
    {
        // Arrange - one byte over the limit
        var header = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(header, 10000);
        var buffer = new ReadOnlySequence<byte>(header);

        // Act
        var result = FrameReader.TryParseFrame(buffer, 9999, out _, out _, out var status);

        // Assert
        Assert.True(result);
        Assert.Equal(FrameStatus.OversizedFrame, status);
    }

    [Fact]
    public void EncodeFrame_ProducesCorrectBigEndianLengthPrefix()
    {
        // Arrange
        var payload = new byte[256]; // 0x0100 in big-endian

        // Act
        var frame = FrameReader.EncodeFrame(payload);

        // Assert
        Assert.Equal(258, frame.Length); // 2 + 256
        Assert.Equal(0x01, frame[0]); // High byte
        Assert.Equal(0x00, frame[1]); // Low byte
    }

    [Fact]
    public void EncodeFrame_SmallPayload_ProducesCorrectPrefix()
    {
        // Arrange
        var payload = new byte[5]; // 0x0005 in big-endian

        // Act
        var frame = FrameReader.EncodeFrame(payload);

        // Assert
        Assert.Equal(7, frame.Length); // 2 + 5
        Assert.Equal(0x00, frame[0]); // High byte
        Assert.Equal(0x05, frame[1]); // Low byte
    }

    [Fact]
    public async Task ReadFrameAsync_WithCompleteFrame_ReturnsSuccess()
    {
        // Arrange
        var payload = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        var frame = FrameReader.EncodeFrame(payload);

        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(frame);
        await pipe.Writer.CompleteAsync();

        // Act
        var result = await FrameReader.ReadFrameAsync(
            pipe.Reader, 9999, TimeSpan.FromSeconds(5), CancellationToken.None);

        // Assert
        Assert.Equal(FrameReadStatus.Success, result.Status);
        Assert.Equal(payload, result.Payload);
    }

    [Fact]
    public async Task ReadFrameAsync_WithConnectionClosed_ReturnsClosed()
    {
        // Arrange - write only partial data then complete
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[] { 0x00, 0x05, 0x01 }); // Header says 5 bytes, only 1 present
        await pipe.Writer.CompleteAsync();

        // Act
        var result = await FrameReader.ReadFrameAsync(
            pipe.Reader, 9999, TimeSpan.FromSeconds(5), CancellationToken.None);

        // Assert
        Assert.Equal(FrameReadStatus.ConnectionClosed, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task ReadFrameAsync_WithOversizedFrame_ReturnsOversized()
    {
        // Arrange - header declares 1000 bytes but max is 100
        var header = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(header, 1000);

        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(header);
        await pipe.Writer.CompleteAsync();

        // Act
        var result = await FrameReader.ReadFrameAsync(
            pipe.Reader, 100, TimeSpan.FromSeconds(5), CancellationToken.None);

        // Assert
        Assert.Equal(FrameReadStatus.OversizedFrame, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task ReadFrameAsync_WithTimeout_ReturnsTimedOut()
    {
        // Arrange - write nothing, timeout quickly
        var pipe = new Pipe();
        // Don't write anything and don't complete the writer

        // Act
        var result = await FrameReader.ReadFrameAsync(
            pipe.Reader, 9999, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        // Assert
        Assert.Equal(FrameReadStatus.Timeout, result.Status);
        Assert.Null(result.Payload);

        // Cleanup
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task ReadFrameAsync_WithCancellation_ThrowsOperationCanceled()
    {
        // Arrange
        var pipe = new Pipe();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FrameReader.ReadFrameAsync(pipe.Reader, 9999, TimeSpan.FromSeconds(30), cts.Token));

        // Cleanup
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public void TryParseFrame_WithMultiSegmentBuffer_ParsesCorrectly()
    {
        // Arrange - create a multi-segment buffer where header spans segments
        var firstSegment = new byte[] { 0x00 }; // First byte of header
        var secondSegment = new byte[] { 0x03, 0xAA, 0xBB, 0xCC }; // Second header byte + payload

        var first = new BufferSegment(firstSegment);
        var last = first.Append(secondSegment);
        var buffer = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

        // Act
        var result = FrameReader.TryParseFrame(buffer, 9999, out var payload, out _, out var status);

        // Assert
        Assert.True(result);
        Assert.Equal(FrameStatus.Complete, status);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, payload);
    }

    /// <summary>
    /// Helper class to create multi-segment ReadOnlySequence buffers for testing.
    /// </summary>
    private class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new BufferSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = segment;
            return segment;
        }
    }
}
