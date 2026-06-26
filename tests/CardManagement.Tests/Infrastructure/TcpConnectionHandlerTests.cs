using System.Buffers;
using System.IO.Pipelines;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Tcp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for TcpConnectionHandler verifying the wiring:
/// TcpListenerService → TcpConnectionHandler → Iso8583GatewayAdapter → CardProcessorRouter → TransactionPipeline
/// </summary>
public class TcpConnectionHandlerTests
{
    private static TcpConnectionHandler CreateHandler(
        IIso8583Gateway? gateway = null,
        ICardProcessorRouter? router = null,
        ITransactionPipeline? pipeline = null,
        INetworkManagementHandler? networkHandler = null)
    {
        return new TcpConnectionHandler(
            gateway ?? new FakeIso8583Gateway(),
            router ?? new FakeCardProcessorRouter(),
            pipeline ?? new FakeTransactionPipeline(),
            networkHandler ?? new FakeNetworkManagementHandler(),
            NullLogger<TcpConnectionHandler>.Instance);
    }

    #region Network Management (MTI 0800)

    [Fact]
    public async Task HandleConnectionAsync_NetworkManagement0800_DelegatesToNetworkManagementHandler()
    {
        var networkHandler = new FakeNetworkManagementHandler();
        var gateway = new FakeIso8583Gateway();
        gateway.SetParseResult(new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "001" }
        });

        var handler = CreateHandler(gateway: gateway, networkHandler: networkHandler);

        var (reader, writer) = CreatePipeReaderWriter(new byte[] { 0x01, 0x02 });
        await handler.HandleConnectionAsync(reader, writer, CancellationToken.None);

        Assert.True(networkHandler.WasHandleNetworkManagementCalled);
    }

    [Fact]
    public async Task HandleConnectionAsync_NetworkManagement0800_WritesResponseToPipeWriter()
    {
        var networkHandler = new FakeNetworkManagementHandler();
        var gateway = new FakeIso8583Gateway();
        gateway.SetParseResult(new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "001" }
        });
        gateway.SetConstructResult(new byte[] { 0xAA, 0xBB });

        var handler = CreateHandler(gateway: gateway, networkHandler: networkHandler);

        var (reader, writer) = CreatePipeReaderWriter(new byte[] { 0x01, 0x02 });
        var responsePipe = new Pipe();
        await handler.HandleConnectionAsync(reader, responsePipe.Writer, CancellationToken.None);
        await responsePipe.Writer.CompleteAsync();

        var readResult = await responsePipe.Reader.ReadAsync();
        Assert.True(readResult.Buffer.Length > 0);
        await responsePipe.Reader.CompleteAsync();
    }

    #endregion

    #region Authorization (MTI 0100)

    [Fact]
    public async Task HandleConnectionAsync_Authorization0100_RoutesViaProcessorAndDispatchesToPipeline()
    {
        var pipeline = new FakeTransactionPipeline();
        var router = new FakeCardProcessorRouter(signedOn: true);
        var networkHandler = new FakeNetworkManagementHandler(signedOn: true);
        var gateway = new FakeIso8583Gateway();
        gateway.SetParseResult(new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = "5061234567890123", // PAN with known BIN
                [4] = "10000",
                [14] = "2512",
                [49] = "566"
            }
        });
        gateway.SetConstructResult(new byte[] { 0xCC, 0xDD });

        var handler = CreateHandler(gateway: gateway, router: router, pipeline: pipeline, networkHandler: networkHandler);

        var (reader, writer) = CreatePipeReaderWriter(new byte[] { 0x01 });
        var responsePipe = new Pipe();
        await handler.HandleConnectionAsync(reader, responsePipe.Writer, CancellationToken.None);

        Assert.True(pipeline.WasProcessAuthorizationCalled);
        Assert.False(pipeline.WasProcessReversalCalled);
    }

    [Fact]
    public async Task HandleConnectionAsync_Authorization0100_NotSignedOn_RejectsWithSignOnRequired()
    {
        var pipeline = new FakeTransactionPipeline();
        var router = new FakeCardProcessorRouter(signedOn: false);
        var networkHandler = new FakeNetworkManagementHandler(signedOn: false);
        var gateway = new FakeIso8583Gateway();
        gateway.SetParseResult(new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = "5061234567890123",
                [4] = "10000",
                [14] = "2512",
                [49] = "566"
            }
        });
        gateway.SetConstructResult(new byte[] { 0xEE });

        var handler = CreateHandler(gateway: gateway, router: router, pipeline: pipeline, networkHandler: networkHandler);

        var (reader, writer) = CreatePipeReaderWriter(new byte[] { 0x01 });
        var responsePipe = new Pipe();
        await handler.HandleConnectionAsync(reader, responsePipe.Writer, CancellationToken.None);

        // Pipeline should NOT be called when processor is not signed on
        Assert.False(pipeline.WasProcessAuthorizationCalled);
    }

    [Fact]
    public async Task HandleConnectionAsync_Authorization0100_MissingPan_ReturnsError()
    {
        var pipeline = new FakeTransactionPipeline();
        var gateway = new FakeIso8583Gateway();
        gateway.SetParseResult(new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [4] = "10000",  // No PAN (field 2)
                [49] = "566"
            }
        });
        gateway.SetConstructResult(new byte[] { 0xFF });

        var handler = CreateHandler(gateway: gateway, pipeline: pipeline);

        var (reader, writer) = CreatePipeReaderWriter(new byte[] { 0x01 });
        var responsePipe = new Pipe();
        await handler.HandleConnectionAsync(reader, responsePipe.Writer, CancellationToken.None);

        Assert.False(pipeline.WasProcessAuthorizationCalled);
    }

    #endregion

    #region Reversal (MTI 0420)

    [Fact]
    public async Task HandleConnectionAsync_Reversal0420_RoutesViaProcessorAndDispatchesToReversalPipeline()
    {
        var pipeline = new FakeTransactionPipeline();
        var router = new FakeCardProcessorRouter(signedOn: true);
        var networkHandler = new FakeNetworkManagementHandler(signedOn: true);
        var gateway = new FakeIso8583Gateway();
        gateway.SetParseResult(new Iso8583Message
        {
            Mti = "0420",
            Fields = new Dictionary<int, string>
            {
                [2] = "5061234567890123",
                [4] = "5000",
                [11] = "000001"
            }
        });
        gateway.SetConstructResult(new byte[] { 0xAA });

        var handler = CreateHandler(gateway: gateway, router: router, pipeline: pipeline, networkHandler: networkHandler);

        var (reader, writer) = CreatePipeReaderWriter(new byte[] { 0x01 });
        var responsePipe = new Pipe();
        await handler.HandleConnectionAsync(reader, responsePipe.Writer, CancellationToken.None);

        Assert.True(pipeline.WasProcessReversalCalled);
        Assert.False(pipeline.WasProcessAuthorizationCalled);
    }

    [Fact]
    public async Task HandleConnectionAsync_Reversal0420_NotSignedOn_RejectsWithSignOnRequired()
    {
        var pipeline = new FakeTransactionPipeline();
        var router = new FakeCardProcessorRouter(signedOn: false);
        var networkHandler = new FakeNetworkManagementHandler(signedOn: false);
        var gateway = new FakeIso8583Gateway();
        gateway.SetParseResult(new Iso8583Message
        {
            Mti = "0420",
            Fields = new Dictionary<int, string>
            {
                [2] = "5061234567890123",
                [4] = "5000",
                [11] = "000001"
            }
        });
        gateway.SetConstructResult(new byte[] { 0xEE });

        var handler = CreateHandler(gateway: gateway, router: router, pipeline: pipeline, networkHandler: networkHandler);

        var (reader, writer) = CreatePipeReaderWriter(new byte[] { 0x01 });
        var responsePipe = new Pipe();
        await handler.HandleConnectionAsync(reader, responsePipe.Writer, CancellationToken.None);

        Assert.False(pipeline.WasProcessReversalCalled);
    }

    #endregion

    #region Parse Failure

    [Fact]
    public async Task HandleConnectionAsync_ParseFailure_WritesErrorResponse()
    {
        var gateway = new FakeIso8583Gateway();
        gateway.SetParseFailure("System malfunction", "96");
        gateway.SetConstructResult(new byte[] { 0x96 });

        var handler = CreateHandler(gateway: gateway);

        var (reader, writer) = CreatePipeReaderWriter(new byte[] { 0xFF, 0xFE });
        var responsePipe = new Pipe();
        await handler.HandleConnectionAsync(reader, responsePipe.Writer, CancellationToken.None);
        await responsePipe.Writer.CompleteAsync();

        var readResult = await responsePipe.Reader.ReadAsync();
        Assert.True(readResult.Buffer.Length > 0);
        await responsePipe.Reader.CompleteAsync();
    }

    #endregion

    #region Empty Buffer

    [Fact]
    public async Task HandleConnectionAsync_EmptyBuffer_ReturnsImmediately()
    {
        var pipeline = new FakeTransactionPipeline();
        var handler = CreateHandler(pipeline: pipeline);

        var (reader, writer) = CreatePipeReaderWriter(Array.Empty<byte>());
        var responsePipe = new Pipe();
        await handler.HandleConnectionAsync(reader, responsePipe.Writer, CancellationToken.None);
        await responsePipe.Writer.CompleteAsync();

        Assert.False(pipeline.WasProcessAuthorizationCalled);
        Assert.False(pipeline.WasProcessReversalCalled);
    }

    #endregion

    #region Unsupported MTI

    [Fact]
    public async Task HandleConnectionAsync_UnsupportedMti_WritesErrorResponse()
    {
        var gateway = new FakeIso8583Gateway();
        gateway.SetParseResult(new Iso8583Message
        {
            Mti = "0200", // Unsupported
            Fields = new Dictionary<int, string>()
        });
        gateway.SetConstructResult(new byte[] { 0x96 });

        var handler = CreateHandler(gateway: gateway);

        var (reader, writer) = CreatePipeReaderWriter(new byte[] { 0x01 });
        var responsePipe = new Pipe();
        await handler.HandleConnectionAsync(reader, responsePipe.Writer, CancellationToken.None);
        await responsePipe.Writer.CompleteAsync();

        var readResult = await responsePipe.Reader.ReadAsync();
        Assert.True(readResult.Buffer.Length > 0);
        await responsePipe.Reader.CompleteAsync();
    }

    #endregion

    #region Helpers

    private static (PipeReader reader, PipeWriter writer) CreatePipeReaderWriter(byte[] data)
    {
        var pipe = new Pipe();
        var writeTask = Task.Run(async () =>
        {
            if (data.Length > 0)
            {
                await pipe.Writer.WriteAsync(data);
            }
            await pipe.Writer.CompleteAsync();
        });
        writeTask.Wait();
        return (pipe.Reader, pipe.Writer);
    }

    #endregion

    #region Fakes

    private class FakeIso8583Gateway : IIso8583Gateway
    {
        private Result<Iso8583Message>? _parseResult;
        private Result<byte[]>? _constructResult;

        public void SetParseResult(Iso8583Message message)
        {
            _parseResult = Result<Iso8583Message>.Success(message);
        }

        public void SetParseFailure(string error, string errorCode)
        {
            _parseResult = Result<Iso8583Message>.Failure(error, errorCode);
        }

        public void SetConstructResult(byte[] bytes)
        {
            _constructResult = Result<byte[]>.Success(bytes);
        }

        public Result<Iso8583Message> Parse(ReadOnlySpan<byte> rawMessage)
        {
            return _parseResult ?? Result<Iso8583Message>.Failure("No parse result configured", "96");
        }

        public Result<byte[]> Construct(Iso8583Message message)
        {
            return _constructResult ?? Result<byte[]>.Success(Array.Empty<byte>());
        }

        public bool IsValidFrameSize(int messageLength)
        {
            return messageLength <= 9999;
        }
    }

    private class FakeCardProcessorRouter : ICardProcessorRouter
    {
        private readonly bool _signedOn;

        public FakeCardProcessorRouter(bool signedOn = false)
        {
            _signedOn = signedOn;
        }

        public ProcessorType ResolveProcessor(string pan)
        {
            // Simple BIN routing: 506 → Interswitch/Verve, 4/5 → CardFi
            if (pan.StartsWith("506"))
                return ProcessorType.Interswitch;
            return ProcessorType.CardFi;
        }

        public bool IsSignedOn(ProcessorType processor)
        {
            return _signedOn;
        }

        public Task<Result> ProcessNetworkManagement(Iso8583Message message, ProcessorType processor)
        {
            return Task.FromResult(Result.Success());
        }
    }

    private class FakeTransactionPipeline : ITransactionPipeline
    {
        public bool WasProcessAuthorizationCalled { get; private set; }
        public bool WasProcessReversalCalled { get; private set; }
        public Iso8583Message? LastAuthorizationRequest { get; private set; }
        public Iso8583Message? LastReversalRequest { get; private set; }

        public Task<Iso8583Message> ProcessAuthorizationAsync(Iso8583Message request, CancellationToken ct)
        {
            WasProcessAuthorizationCalled = true;
            LastAuthorizationRequest = request;
            return Task.FromResult(new Iso8583Message
            {
                Mti = "0110",
                Fields = new Dictionary<int, string> { [39] = "00" }
            });
        }

        public Task<Iso8583Message> ProcessReversalAsync(Iso8583Message request, CancellationToken ct)
        {
            WasProcessReversalCalled = true;
            LastReversalRequest = request;
            return Task.FromResult(new Iso8583Message
            {
                Mti = "0430",
                Fields = new Dictionary<int, string> { [39] = "00" }
            });
        }
    }

    private class FakeNetworkManagementHandler : INetworkManagementHandler
    {
        private readonly bool _signedOn;
        public bool WasHandleNetworkManagementCalled { get; private set; }

        public FakeNetworkManagementHandler(bool signedOn = false)
        {
            _signedOn = signedOn;
        }

        public Task<Result<Iso8583Message>> HandleNetworkManagementAsync(
            Iso8583Message request, ProcessorType processor, CancellationToken ct = default)
        {
            WasHandleNetworkManagementCalled = true;
            var response = new Iso8583Message
            {
                Mti = "0810",
                Fields = new Dictionary<int, string> { [39] = "00" }
            };
            return Task.FromResult(Result<Iso8583Message>.Success(response));
        }

        public Result ValidateFinancialMessageAllowed(Iso8583Message request, ProcessorType processor)
        {
            if (_signedOn)
                return Result.Success();
            return Result.Failure("Processor not signed on", "SIGN_ON_REQUIRED");
        }
    }

    #endregion
}
