using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Routing;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for BIN Range Routing Correctness (Property 3).
/// Validates: Requirements 3.1, 4.1
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "3")]
public class BinRangeRoutingPropertyTests
{
    private static readonly string BinRangesJson = @"[
        {""prefix"": ""506199"", ""scheme"": ""Verve"", ""panLength"": 19},
        {""prefix"": ""650002"", ""scheme"": ""Verve"", ""panLength"": 19},
        {""prefix"": ""4"", ""scheme"": ""Visa"", ""panLength"": 16},
        {""prefix"": ""51"", ""scheme"": ""Mastercard"", ""panLength"": 16},
        {""prefix"": ""52"", ""scheme"": ""Mastercard"", ""panLength"": 16},
        {""prefix"": ""53"", ""scheme"": ""Mastercard"", ""panLength"": 16}
    ]";

    /// <summary>
    /// Known BIN prefix configurations used by the generators.
    /// Each tuple is (prefix, expectedProcessor, panLength).
    /// </summary>
    private static readonly (string Prefix, ProcessorType ExpectedProcessor, int PanLength)[] KnownBins =
    {
        ("506199", ProcessorType.Interswitch, 19),
        ("650002", ProcessorType.Interswitch, 19),
        ("4", ProcessorType.CardFi, 16),
        ("51", ProcessorType.CardFi, 16),
        ("52", ProcessorType.CardFi, 16),
        ("53", ProcessorType.CardFi, 16),
    };

    /// <summary>
    /// Prefixes that do NOT match any configured BIN range.
    /// These should cause InvalidOperationException.
    /// </summary>
    private static readonly string[] UnrecognizedPrefixes = { "9999", "00", "8888", "7777", "3333", "6000", "99" };

    private static CardProcessorRouter CreateRouter()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CARD__BIN_RANGES"] = BinRangesJson
            })
            .Build();

        return new CardProcessorRouter(config, new InMemoryProcessorSessionRepository());
    }

    /// <summary>
    /// **Validates: Requirements 3.1, 4.1**
    /// 
    /// Property 3a: For any PAN whose leading digits match a configured BIN range,
    /// the Card Processor Router SHALL resolve to the correct processor type
    /// (Interswitch for Verve BINs, CardFi for Visa/Mastercard BINs).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property KnownBinPrefix_ResolvesToCorrectProcessor()
    {
        var gen = from binIndex in Gen.Choose(0, KnownBins.Length - 1)
                  let bin = KnownBins[binIndex]
                  let remainingDigits = bin.PanLength - bin.Prefix.Length
                  from suffix in GenDigitString(remainingDigits)
                  select (Pan: bin.Prefix + suffix, bin.ExpectedProcessor);

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            var router = CreateRouter();
            var result = router.ResolveProcessor(testCase.Pan);
            return (result == testCase.ExpectedProcessor)
                .Label($"PAN '{testCase.Pan}' should route to {testCase.ExpectedProcessor} but got {result}");
        });
    }

    /// <summary>
    /// **Validates: Requirements 3.1, 4.1**
    /// 
    /// Property 3b: For any PAN whose leading digits do NOT match any configured BIN range,
    /// routing SHALL fail with an InvalidOperationException (unrecognized-BIN error).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UnrecognizedBinPrefix_ThrowsInvalidOperationException()
    {
        var gen = from prefixIndex in Gen.Choose(0, UnrecognizedPrefixes.Length - 1)
                  let prefix = UnrecognizedPrefixes[prefixIndex]
                  from panLength in Gen.Choose(16, 19)
                  let remainingDigits = Math.Max(0, panLength - prefix.Length)
                  from suffix in GenDigitString(remainingDigits)
                  select prefix + suffix;

        return Prop.ForAll(gen.ToArbitrary(), pan =>
        {
            var router = CreateRouter();
            var threw = false;
            try
            {
                router.ResolveProcessor(pan);
            }
            catch (InvalidOperationException ex)
            {
                threw = true;
                return ex.Message.Contains("does not match any configured BIN range")
                    .Label($"Exception message should mention BIN range mismatch for PAN '{pan}'");
            }

            return threw.Label($"Expected InvalidOperationException for unrecognized PAN '{pan}' but no exception was thrown");
        });
    }

    /// <summary>
    /// Generates a string of exactly the specified number of random digits ('0'-'9').
    /// </summary>
    private static Gen<string> GenDigitString(int length)
    {
        if (length <= 0)
            return Gen.Constant(string.Empty);

        return Gen.ArrayOf(length, Gen.Choose(0, 9))
            .Select(digits => string.Concat(digits.Select(d => d.ToString())));
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
