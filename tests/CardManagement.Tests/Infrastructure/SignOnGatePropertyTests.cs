using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Routing;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for Sign-On Gate Invariant (Property 5).
/// Validates: Requirements 3.7
/// 
/// For any financial transaction message (authorization or reversal) received when the
/// corresponding card processor session has NOT completed a successful sign-on exchange,
/// the system SHALL reject the message without processing it through the transaction pipeline.
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "5")]
public class SignOnGatePropertyTests
{
    /// <summary>
    /// Financial MTIs: authorization request/response and reversal request/response.
    /// </summary>
    private static readonly string[] FinancialMtis = { "0100", "0110", "0420", "0430" };

    private static CardProcessorRouter CreateRouter()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CARD__BIN_RANGES"] = @"[
                    {""prefix"": ""506199"", ""scheme"": ""Verve"", ""panLength"": 19},
                    {""prefix"": ""4"", ""scheme"": ""Visa"", ""panLength"": 16}
                ]"
            })
            .Build();

        return new CardProcessorRouter(config, new InMemoryProcessorSessionRepository());
    }

    private static NetworkManagementHandler CreateHandler(CardProcessorRouter router)
    {
        var logger = NullLogger<NetworkManagementHandler>.Instance;
        return new NetworkManagementHandler(router, logger);
    }

    /// <summary>
    /// Generator for a random financial MTI.
    /// </summary>
    private static Gen<string> GenFinancialMti() =>
        Gen.Elements(FinancialMtis);

    /// <summary>
    /// Generator for a random ProcessorType.
    /// </summary>
    private static Gen<ProcessorType> GenProcessorType() =>
        Gen.Elements(ProcessorType.Interswitch, ProcessorType.CardFi);

    /// <summary>
    /// Generator for a random sign-on state (true = signed on, false = not signed on).
    /// </summary>
    private static Gen<bool> GenSignOnState() =>
        Arb.Generate<bool>();

    /// <summary>
    /// Generates a minimal financial Iso8583Message with a random financial MTI.
    /// </summary>
    private static Gen<Iso8583Message> GenFinancialMessage() =>
        from mti in GenFinancialMti()
        select new Iso8583Message
        {
            Mti = mti,
            Fields = new Dictionary<int, string>
            {
                [2] = "5061990000000001",  // PAN
                [3] = "000000",             // Processing code
                [4] = "000000001000",       // Amount
                [11] = "123456"             // STAN
            }
        };

    /// <summary>
    /// **Validates: Requirements 3.7**
    /// 
    /// Property 5: Sign-On Gate Invariant
    /// Generate financial messages (auth/reversal) with random sign-on state (true/false).
    /// When not signed on: assert result.IsSuccess is false and error code is "SIGN_ON_REQUIRED".
    /// When signed on: assert result.IsSuccess is true.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FinancialMessages_RejectedWhenNotSignedOn_AllowedWhenSignedOn()
    {
        var gen = from message in GenFinancialMessage()
                  from processor in GenProcessorType()
                  from isSignedOn in GenSignOnState()
                  select (Message: message, Processor: processor, IsSignedOn: isSignedOn);

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            var router = CreateRouter();
            var handler = CreateHandler(router);

            // Set sign-on state by performing a sign-on if needed
            if (testCase.IsSignedOn)
            {
                var signOnMessage = new Iso8583Message
                {
                    Mti = "0800",
                    Fields = new Dictionary<int, string>
                    {
                        [70] = "001" // Sign-on function code
                    }
                };
                router.ProcessNetworkManagement(signOnMessage, testCase.Processor).GetAwaiter().GetResult();
            }

            var result = handler.ValidateFinancialMessageAllowed(testCase.Message, testCase.Processor);

            if (!testCase.IsSignedOn)
            {
                return (!result.IsSuccess && result.ErrorCode == "SIGN_ON_REQUIRED")
                    .Label($"Financial message (MTI {testCase.Message.Mti}) to {testCase.Processor} " +
                           $"should be rejected with SIGN_ON_REQUIRED when not signed on, " +
                           $"but got IsSuccess={result.IsSuccess}, ErrorCode={result.ErrorCode}");
            }
            else
            {
                return result.IsSuccess
                    .Label($"Financial message (MTI {testCase.Message.Mti}) to {testCase.Processor} " +
                           $"should be allowed when signed on, but got IsSuccess={result.IsSuccess}, " +
                           $"ErrorCode={result.ErrorCode}");
            }
        });
    }

    /// <summary>
    /// In-memory implementation of IProcessorSessionRepository for testing purposes.
    /// </summary>
    private class InMemoryProcessorSessionRepository : IProcessorSessionRepository
    {
        private readonly Dictionary<ProcessorType, ProcessorSession> _sessions = new();

        public Task<ProcessorSession?> GetByTypeAsync(ProcessorType processorType, CancellationToken cancellationToken = default)
        {
            _sessions.TryGetValue(processorType, out var session);
            return Task.FromResult(session);
        }

        public Task UpdateAsync(ProcessorSession session, CancellationToken cancellationToken = default)
        {
            _sessions[session.ProcessorType] = session;
            return Task.CompletedTask;
        }
    }
}
