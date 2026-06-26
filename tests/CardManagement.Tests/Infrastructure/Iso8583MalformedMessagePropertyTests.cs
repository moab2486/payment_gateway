using CardManagement.Infrastructure.Iso8583;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for ISO 8583 Gateway malformed message rejection.
/// 
/// **Validates: Requirements 1.3**
/// 
/// Property 2: Malformed Message Rejection
/// For any byte sequence that is not a valid ISO 8583 message (random bytes, truncated messages,
/// invalid bitmaps), the ISO 8583 Gateway SHALL return a response with response code "96"
/// (system malfunction) and SHALL NOT produce a structured message object.
/// For messages exceeding 9999 bytes, response code "30" (format error) is returned.
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "2")]
public class Iso8583MalformedMessagePropertyTests
{
    private readonly Iso8583GatewayAdapter _adapter;

    public Iso8583MalformedMessagePropertyTests()
    {
        var logger = NullLogger<Iso8583GatewayAdapter>.Instance;
        _adapter = new Iso8583GatewayAdapter(logger);
    }

    /// <summary>
    /// Property 2: Malformed Message Rejection
    /// 
    /// For random byte arrays of length 1-9999 that are NOT valid ISO 8583,
    /// the gateway returns response code "96" (system malfunction)
    /// and does not produce a structured message.
    /// 
    /// For random byte arrays of length > 9999, the gateway returns response code "30"
    /// (format error) and does not produce a structured message.
    /// 
    /// **Validates: Requirements 1.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MalformedMessageArbitrary) })]
    public void MalformedBytes_AreRejected_WithAppropriateResponseCode(MalformedPayload payload)
    {
        // Act
        var result = _adapter.Parse(payload.Bytes);

        // Assert - result must indicate failure (no structured message produced)
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);

        if (payload.Bytes.Length > Iso8583GatewayAdapter.MaxFrameSize)
        {
            // Oversized messages get response code "30" (format error)
            Assert.Equal(Iso8583GatewayAdapter.ResponseCodeFormatError, result.ErrorCode);
        }
        else
        {
            // Malformed messages within size limits get response code "96" (system malfunction)
            Assert.Equal(Iso8583GatewayAdapter.ResponseCodeSystemMalfunction, result.ErrorCode);
        }
    }

    /// <summary>
    /// Wrapper type for generated malformed byte payloads.
    /// </summary>
    public class MalformedPayload
    {
        public byte[] Bytes { get; }

        public MalformedPayload(byte[] bytes)
        {
            Bytes = bytes;
        }

        public override string ToString()
        {
            return $"MalformedPayload[Length={Bytes.Length}, First4Bytes={Convert.ToHexString(Bytes.AsSpan(0, Math.Min(4, Bytes.Length)))}]";
        }
    }

    /// <summary>
    /// Custom FsCheck arbitrary for generating malformed byte arrays that are NOT valid ISO 8583.
    /// Generates random byte arrays of length 1-10000 using strategies that produce
    /// data unlikely to parse as valid ISO 8583 messages.
    /// </summary>
    public class MalformedMessageArbitrary
    {
        public static Arbitrary<MalformedPayload> MalformedPayloadArbitrary()
        {
            var gen = Gen.Frequency(
                // Strategy 1: Completely random bytes, length 1-9999 (within frame limit)
                Tuple.Create(4, GenRandomBytes(1, 9999)),
                // Strategy 2: Oversized messages (> 9999 bytes), triggers "30" response
                Tuple.Create(1, GenRandomBytes(10000, 10000)),
                // Strategy 3: Very short garbage (1-10 bytes) - too short to be valid ISO 8583
                Tuple.Create(2, GenRandomBytes(1, 10)),
                // Strategy 4: Bytes with invalid MTI area (high-value bytes in first 4 positions)
                Tuple.Create(2, GenInvalidMtiBytes()),
                // Strategy 5: All zeros (invalid bitmap, no valid MTI)
                Tuple.Create(1, GenAllZeros(1, 9999))
            );

            return Arb.From(gen);
        }

        private static Gen<MalformedPayload> GenRandomBytes(int minLen, int maxLen)
        {
            return Gen.Choose(minLen, maxLen).SelectMany(length =>
                Gen.ArrayOf(length, Arb.Generate<byte>()).Select(bytes =>
                    new MalformedPayload(bytes)));
        }

        private static Gen<MalformedPayload> GenInvalidMtiBytes()
        {
            // Generate bytes where the first 4 bytes are high-value (0x80-0xFF)
            // which cannot form a valid ISO 8583 MTI (ASCII digits expected)
            return Gen.Choose(20, 200).SelectMany(length =>
            {
                var highBytes = Gen.ArrayOf(4, Gen.Choose(0x80, 0xFF).Select(i => (byte)i));
                var restBytes = Gen.ArrayOf(length - 4 > 0 ? length - 4 : 0, Arb.Generate<byte>());

                return highBytes.SelectMany(h => restBytes.Select(r =>
                {
                    var combined = new byte[h.Length + r.Length];
                    Array.Copy(h, 0, combined, 0, h.Length);
                    Array.Copy(r, 0, combined, h.Length, r.Length);
                    return new MalformedPayload(combined);
                }));
            });
        }

        private static Gen<MalformedPayload> GenAllZeros(int minLen, int maxLen)
        {
            return Gen.Choose(minLen, maxLen).Select(length =>
                new MalformedPayload(new byte[length]));
        }
    }
}
