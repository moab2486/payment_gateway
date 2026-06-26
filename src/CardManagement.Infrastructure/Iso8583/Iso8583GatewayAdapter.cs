using System.Text;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using Microsoft.Extensions.Logging;
using NetCore8583;
using NetCore8583.Parse;

namespace CardManagement.Infrastructure.Iso8583;

/// <summary>
/// ISO 8583 gateway adapter implementing IIso8583Gateway using the NetCore8583 library.
/// Supports bitmap-driven field encoding/decoding for ISO 8583:1987 and ISO 8583:1993.
/// </summary>
public sealed class Iso8583GatewayAdapter : IIso8583Gateway
{
    /// <summary>
    /// Maximum allowed ISO 8583 message size in bytes.
    /// Messages exceeding this limit are rejected with response code "30".
    /// </summary>
    public const int MaxFrameSize = 9999;

    /// <summary>
    /// Response code for format errors (oversized messages).
    /// </summary>
    public const string ResponseCodeFormatError = "30";

    /// <summary>
    /// Response code for system malfunction (parse failures).
    /// </summary>
    public const string ResponseCodeSystemMalfunction = "96";

    private readonly MessageFactory<IsoMessage> _messageFactory;
    private readonly ILogger<Iso8583GatewayAdapter> _logger;

    public Iso8583GatewayAdapter(ILogger<Iso8583GatewayAdapter> logger)
    {
        _logger = logger;
        _messageFactory = ConfigureMessageFactory();
    }

    /// <inheritdoc />
    public Result<Iso8583Message> Parse(ReadOnlySpan<byte> rawMessage)
    {
        if (!IsValidFrameSize(rawMessage.Length))
        {
            _logger.LogWarning(
                "Inbound ISO 8583 message rejected: size {Size} exceeds max {MaxSize}",
                rawMessage.Length, MaxFrameSize);
            return Result<Iso8583Message>.Failure(
                $"Message size {rawMessage.Length} exceeds maximum {MaxFrameSize} bytes.",
                ResponseCodeFormatError);
        }

        try
        {
            byte[] messageBytes = rawMessage.ToArray();

            // NetCore8583 uses sbyte[] (Java convention) - convert from byte[]
            sbyte[] signedBytes = new sbyte[messageBytes.Length];
            Buffer.BlockCopy(messageBytes, 0, signedBytes, 0, messageBytes.Length);

            IsoMessage isoMessage = _messageFactory.ParseMessage(signedBytes, 0);

            if (isoMessage == null)
            {
                _logger.LogWarning(
                    "ISO 8583 parse returned null. Raw bytes (hex): {RawHex}",
                    Convert.ToHexString(messageBytes));
                return Result<Iso8583Message>.Failure(
                    "Failed to parse ISO 8583 message: parser returned null.",
                    ResponseCodeSystemMalfunction);
            }

            var fields = new Dictionary<int, string>();
            for (int fieldNum = 2; fieldNum <= 128; fieldNum++)
            {
                if (isoMessage.HasField(fieldNum))
                {
                    var isoValue = isoMessage.GetField(fieldNum);
                    if (isoValue != null)
                    {
                        fields[fieldNum] = isoValue.ToString() ?? string.Empty;
                    }
                }
            }

            string mti = isoMessage.Type.ToString("X4");

            var result = new Iso8583Message
            {
                Mti = mti,
                Fields = fields,
                RawBytes = messageBytes
            };

            _logger.LogDebug(
                "Parsed ISO 8583 message MTI {Mti}, {FieldCount} fields",
                mti, fields.Count);

            return Result<Iso8583Message>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ISO 8583 parse failure. Raw bytes (hex): {RawHex}",
                Convert.ToHexString(rawMessage.ToArray()));

            return Result<Iso8583Message>.Failure(
                $"Failed to parse ISO 8583 message: {ex.Message}",
                ResponseCodeSystemMalfunction);
        }
    }

    /// <inheritdoc />
    public Result<byte[]> Construct(Iso8583Message message)
    {
        try
        {
            if (!int.TryParse(message.Mti,
                System.Globalization.NumberStyles.HexNumber, null, out int mtiValue))
            {
                return Result<byte[]>.Failure(
                    $"Invalid MTI format: '{message.Mti}'. Expected 4-digit hex.",
                    ResponseCodeFormatError);
            }

            var isoMessage = _messageFactory.NewMessage(mtiValue);

            foreach (var (fieldNum, fieldValue) in message.Fields)
            {
                if (fieldNum < 2 || fieldNum > 128) continue;

                var isoType = ResolveFieldType(fieldNum);
                int length = ResolveFieldLength(fieldNum, isoType);

                isoMessage.SetValue(fieldNum, fieldValue, isoType, length);
            }

            // WriteData returns sbyte[] - convert to byte[]
            sbyte[] signedData = isoMessage.WriteData();
            byte[] encodedBytes = new byte[signedData.Length];
            Buffer.BlockCopy(signedData, 0, encodedBytes, 0, signedData.Length);

            if (!IsValidFrameSize(encodedBytes.Length))
            {
                return Result<byte[]>.Failure(
                    $"Constructed message size {encodedBytes.Length} exceeds max {MaxFrameSize}.",
                    ResponseCodeFormatError);
            }

            _logger.LogDebug(
                "Constructed ISO 8583 message MTI {Mti}, size {Size} bytes",
                message.Mti, encodedBytes.Length);

            return Result<byte[]>.Success(encodedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to construct ISO 8583 message MTI {Mti}", message.Mti);
            return Result<byte[]>.Failure(
                $"Failed to construct ISO 8583 message: {ex.Message}",
                ResponseCodeSystemMalfunction);
        }
    }

    /// <inheritdoc />
    public bool IsValidFrameSize(int messageLength)
    {
        return messageLength >= 0 && messageLength <= MaxFrameSize;
    }

    /// <summary>
    /// Configures the MessageFactory with parse maps for supported MTIs.
    /// </summary>
    private static MessageFactory<IsoMessage> ConfigureMessageFactory()
    {
        var factory = new MessageFactory<IsoMessage>
        {
            Encoding = Encoding.UTF8,
            UseBinaryMessages = false,
            UseBinaryBitmap = false,
            EnforceSecondBitmap = true
        };

        // Authorization request (0100)
        factory.SetParseMap(0x0100, BuildAuthorizationParseMap());
        // Authorization response (0110)
        factory.SetParseMap(0x0110, BuildAuthorizationParseMap());
        // Reversal request (0420)
        factory.SetParseMap(0x0420, BuildReversalParseMap());
        // Reversal response (0430)
        factory.SetParseMap(0x0430, BuildReversalResponseParseMap());
        // Network management request (0800)
        factory.SetParseMap(0x0800, BuildNetworkManagementParseMap());
        // Network management response (0810)
        factory.SetParseMap(0x0810, BuildNetworkManagementParseMap());

        return factory;
    }

    private static Dictionary<int, FieldParseInfo> BuildAuthorizationParseMap()
    {
        var encoding = Encoding.UTF8;
        return new Dictionary<int, FieldParseInfo>
        {
            [2] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [3] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 6, encoding),
            [4] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 12, encoding),
            [7] = FieldParseInfo.GetInstance(IsoType.DATE10, 0, encoding),
            [11] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 6, encoding),
            [12] = FieldParseInfo.GetInstance(IsoType.TIME, 0, encoding),
            [13] = FieldParseInfo.GetInstance(IsoType.DATE4, 0, encoding),
            [14] = FieldParseInfo.GetInstance(IsoType.DATE_EXP, 0, encoding),
            [22] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 3, encoding),
            [23] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 3, encoding),
            [25] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 2, encoding),
            [26] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 2, encoding),
            [28] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 9, encoding),
            [32] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [33] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [35] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [37] = FieldParseInfo.GetInstance(IsoType.ALPHA, 12, encoding),
            [38] = FieldParseInfo.GetInstance(IsoType.ALPHA, 6, encoding),
            [39] = FieldParseInfo.GetInstance(IsoType.ALPHA, 2, encoding),
            [40] = FieldParseInfo.GetInstance(IsoType.ALPHA, 3, encoding),
            [41] = FieldParseInfo.GetInstance(IsoType.ALPHA, 8, encoding),
            [42] = FieldParseInfo.GetInstance(IsoType.ALPHA, 15, encoding),
            [43] = FieldParseInfo.GetInstance(IsoType.ALPHA, 40, encoding),
            [49] = FieldParseInfo.GetInstance(IsoType.ALPHA, 3, encoding),
            [52] = FieldParseInfo.GetInstance(IsoType.BINARY, 8, encoding),
            [54] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [55] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [56] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [59] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [60] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [61] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [62] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [63] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [90] = FieldParseInfo.GetInstance(IsoType.ALPHA, 42, encoding),
            [95] = FieldParseInfo.GetInstance(IsoType.ALPHA, 42, encoding),
            [100] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [102] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [103] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [123] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [124] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [125] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [126] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [127] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [128] = FieldParseInfo.GetInstance(IsoType.BINARY, 8, encoding)
        };
    }

    private static Dictionary<int, FieldParseInfo> BuildReversalParseMap()
    {
        var encoding = Encoding.UTF8;
        return new Dictionary<int, FieldParseInfo>
        {
            [2] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [3] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 6, encoding),
            [4] = FieldParseInfo.GetInstance(IsoType.AMOUNT, 0, encoding),
            [7] = FieldParseInfo.GetInstance(IsoType.DATE10, 0, encoding),
            [11] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 6, encoding),
            [12] = FieldParseInfo.GetInstance(IsoType.TIME, 0, encoding),
            [13] = FieldParseInfo.GetInstance(IsoType.DATE4, 0, encoding),
            [14] = FieldParseInfo.GetInstance(IsoType.DATE_EXP, 0, encoding),
            [22] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 3, encoding),
            [25] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 2, encoding),
            [32] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [33] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [35] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [37] = FieldParseInfo.GetInstance(IsoType.ALPHA, 12, encoding),
            [38] = FieldParseInfo.GetInstance(IsoType.ALPHA, 6, encoding),
            [39] = FieldParseInfo.GetInstance(IsoType.ALPHA, 2, encoding),
            [41] = FieldParseInfo.GetInstance(IsoType.ALPHA, 8, encoding),
            [42] = FieldParseInfo.GetInstance(IsoType.ALPHA, 15, encoding),
            [43] = FieldParseInfo.GetInstance(IsoType.ALPHA, 40, encoding),
            [49] = FieldParseInfo.GetInstance(IsoType.ALPHA, 3, encoding),
            [54] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [55] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [56] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [59] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [60] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [90] = FieldParseInfo.GetInstance(IsoType.ALPHA, 42, encoding),
            [95] = FieldParseInfo.GetInstance(IsoType.ALPHA, 42, encoding),
            [100] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [102] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [123] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [125] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [126] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [127] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [128] = FieldParseInfo.GetInstance(IsoType.BINARY, 8, encoding)
        };
    }

    private static Dictionary<int, FieldParseInfo> BuildReversalResponseParseMap()
    {
        var encoding = Encoding.UTF8;
        return new Dictionary<int, FieldParseInfo>
        {
            [2] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [3] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 6, encoding),
            [4] = FieldParseInfo.GetInstance(IsoType.AMOUNT, 0, encoding),
            [7] = FieldParseInfo.GetInstance(IsoType.DATE10, 0, encoding),
            [11] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 6, encoding),
            [12] = FieldParseInfo.GetInstance(IsoType.TIME, 0, encoding),
            [13] = FieldParseInfo.GetInstance(IsoType.DATE4, 0, encoding),
            [25] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 2, encoding),
            [32] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [37] = FieldParseInfo.GetInstance(IsoType.ALPHA, 12, encoding),
            [38] = FieldParseInfo.GetInstance(IsoType.ALPHA, 6, encoding),
            [39] = FieldParseInfo.GetInstance(IsoType.ALPHA, 2, encoding),
            [41] = FieldParseInfo.GetInstance(IsoType.ALPHA, 8, encoding),
            [42] = FieldParseInfo.GetInstance(IsoType.ALPHA, 15, encoding),
            [49] = FieldParseInfo.GetInstance(IsoType.ALPHA, 3, encoding),
            [59] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [60] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [100] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [123] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [127] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [128] = FieldParseInfo.GetInstance(IsoType.BINARY, 8, encoding)
        };
    }

    private static Dictionary<int, FieldParseInfo> BuildNetworkManagementParseMap()
    {
        var encoding = Encoding.UTF8;
        return new Dictionary<int, FieldParseInfo>
        {
            [7] = FieldParseInfo.GetInstance(IsoType.DATE10, 0, encoding),
            [11] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 6, encoding),
            [12] = FieldParseInfo.GetInstance(IsoType.TIME, 0, encoding),
            [13] = FieldParseInfo.GetInstance(IsoType.DATE4, 0, encoding),
            [33] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [37] = FieldParseInfo.GetInstance(IsoType.ALPHA, 12, encoding),
            [39] = FieldParseInfo.GetInstance(IsoType.ALPHA, 2, encoding),
            [41] = FieldParseInfo.GetInstance(IsoType.ALPHA, 8, encoding),
            [53] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [70] = FieldParseInfo.GetInstance(IsoType.NUMERIC, 3, encoding),
            [100] = FieldParseInfo.GetInstance(IsoType.LLVAR, 0, encoding),
            [123] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [127] = FieldParseInfo.GetInstance(IsoType.LLLVAR, 0, encoding),
            [128] = FieldParseInfo.GetInstance(IsoType.BINARY, 8, encoding)
        };
    }

    /// <summary>
    /// Resolves the IsoType for a given field number based on ISO 8583 spec.
    /// </summary>
    private static IsoType ResolveFieldType(int fieldNum)
    {
        return fieldNum switch
        {
            2 => IsoType.LLVAR,
            3 => IsoType.NUMERIC,
            4 => IsoType.AMOUNT,
            7 => IsoType.DATE10,
            11 => IsoType.NUMERIC,
            12 => IsoType.TIME,
            13 => IsoType.DATE4,
            14 => IsoType.DATE_EXP,
            22 => IsoType.NUMERIC,
            23 => IsoType.NUMERIC,
            25 => IsoType.NUMERIC,
            26 => IsoType.NUMERIC,
            28 => IsoType.NUMERIC,
            32 => IsoType.LLVAR,
            33 => IsoType.LLVAR,
            35 => IsoType.LLVAR,
            37 => IsoType.ALPHA,
            38 => IsoType.ALPHA,
            39 => IsoType.ALPHA,
            40 => IsoType.ALPHA,
            41 => IsoType.ALPHA,
            42 => IsoType.ALPHA,
            43 => IsoType.ALPHA,
            49 => IsoType.ALPHA,
            52 => IsoType.BINARY,
            53 => IsoType.LLVAR,
            54 => IsoType.LLLVAR,
            55 => IsoType.LLLVAR,
            56 => IsoType.LLLVAR,
            59 => IsoType.LLLVAR,
            60 => IsoType.LLLVAR,
            61 => IsoType.LLLVAR,
            62 => IsoType.LLLVAR,
            63 => IsoType.LLLVAR,
            70 => IsoType.NUMERIC,
            90 => IsoType.ALPHA,
            95 => IsoType.ALPHA,
            100 => IsoType.LLVAR,
            102 => IsoType.LLVAR,
            103 => IsoType.LLVAR,
            123 => IsoType.LLLVAR,
            124 => IsoType.LLLVAR,
            125 => IsoType.LLLVAR,
            126 => IsoType.LLLVAR,
            127 => IsoType.LLLVAR,
            128 => IsoType.BINARY,
            _ => IsoType.LLLVAR
        };
    }

    /// <summary>
    /// Returns the fixed length for fixed-length field types, or 0 for variable-length.
    /// </summary>
    private static int ResolveFieldLength(int fieldNum, IsoType isoType)
    {
        if (isoType is IsoType.LLVAR or IsoType.LLLVAR or IsoType.LLLLVAR
            or IsoType.AMOUNT or IsoType.DATE10 or IsoType.DATE4
            or IsoType.TIME or IsoType.DATE_EXP)
        {
            return 0;
        }

        return fieldNum switch
        {
            3 => 6,
            11 => 6,
            22 => 3,
            23 => 3,
            25 => 2,
            26 => 2,
            28 => 9,
            37 => 12,
            38 => 6,
            39 => 2,
            40 => 3,
            41 => 8,
            42 => 15,
            43 => 40,
            49 => 3,
            52 => 8,
            70 => 3,
            90 => 42,
            95 => 42,
            128 => 8,
            _ => 0
        };
    }
}
