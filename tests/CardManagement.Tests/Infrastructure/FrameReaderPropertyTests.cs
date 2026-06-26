using System.Buffers;
using CardManagement.Infrastructure.Tcp;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for the FrameReader TCP frame parsing round-trip.
/// Validates: Requirements 2.4
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "6")]
public class FrameReaderPropertyTests
{
    /// <summary>
    /// Property 6: TCP Frame Parsing Round-Trip
    /// For any byte payload of length 1 to 9999, prepending a 2-byte big-endian length prefix
    /// and feeding the result to the frame reader SHALL extract a payload that is byte-equivalent
    /// to the original.
    /// 
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(FramePayloadArbitrary) })]
    public Property EncodeAndParse_RoundTrip_ProducesOriginalPayload(byte[] payload)
    {
        // Encode the payload with a 2-byte big-endian length prefix
        var frame = FrameReader.EncodeFrame(payload);

        // Create a ReadOnlySequence from the encoded frame
        var buffer = new ReadOnlySequence<byte>(frame);

        // Parse the frame
        var parsed = FrameReader.TryParseFrame(
            buffer,
            maxFrameSize: 9999,
            out var parsedPayload,
            out _,
            out var status);

        // Assert: parsing succeeds, status is Complete, and payload is byte-equivalent
        return (parsed && status == FrameStatus.Complete && parsedPayload != null &&
                parsedPayload.SequenceEqual(payload))
            .ToProperty();
    }
}

/// <summary>
/// Custom FsCheck arbitrary that generates byte arrays of length 1 to 9999.
/// </summary>
public static class FramePayloadArbitrary
{
    public static Arbitrary<byte[]> ByteArray()
    {
        var gen = from length in Gen.Choose(1, 9999)
                  from bytes in Gen.ArrayOf(length, Arb.Generate<byte>())
                  select bytes;

        return Arb.From(gen, ShrinkByteArray);
    }

    private static IEnumerable<byte[]> ShrinkByteArray(byte[] value)
    {
        // Shrink by halving the length
        if (value.Length > 1)
        {
            yield return value[..(value.Length / 2)];
        }

        // Shrink by taking first element only
        if (value.Length > 2)
        {
            yield return value[..1];
        }
    }
}
