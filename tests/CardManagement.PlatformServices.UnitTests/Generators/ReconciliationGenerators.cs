using CardManagement.Domain.ValueObjects;
using FsCheck;
using ProcessorType = CardManagement.Domain.PlatformServices.Reconciliation.ProcessorType;
using MatchStatus = CardManagement.Domain.PlatformServices.Reconciliation.MatchStatus;

namespace CardManagement.PlatformServices.UnitTests.Generators;

/// <summary>
/// FsCheck generators for Reconciliation domain entities.
/// </summary>
public static class ReconciliationGenerators
{
    private static readonly string[] CurrencyCodes = { "NGN", "USD", "GBP", "EUR" };

    public static Arbitrary<Money> MoneyArbitrary()
    {
        return (from amount in Gen.Choose(1, 100_000_000)
                from currencyIndex in Gen.Choose(0, CurrencyCodes.Length - 1)
                select new Money(amount, CurrencyCodes[currencyIndex]))
            .ToArbitrary();
    }

    public static Arbitrary<SettlementLineItem> SettlementLineItemArbitrary()
    {
        return (from id in Arb.Generate<Guid>()
                from batchId in Arb.Generate<Guid>()
                from transRef in Gen.Elements("TXN", "REF", "PAY").Select(prefix => $"{prefix}-{Guid.NewGuid():N}")
                from procRef in Gen.Elements("NIBSS", "ISW", "CRD").Select(prefix => $"{prefix}-{Guid.NewGuid():N}")
                from money in MoneyArbitrary().Generator
                from status in Gen.Elements("completed", "pending", "failed", "reversed")
                from dayOffset in Gen.Choose(0, 365)
                from matchStatus in Gen.Elements(MatchStatus.Unmatched, MatchStatus.Matched, MatchStatus.Mismatched)
                select new SettlementLineItem(
                    id,
                    batchId,
                    transRef,
                    procRef,
                    money,
                    status,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-dayOffset)),
                    matchStatus))
            .ToArbitrary();
    }

    public static Arbitrary<PaymentRequest> PaymentRequestArbitrary()
    {
        return (from id in Arb.Generate<Guid>()
                from transRef in Gen.Elements("TXN", "REF", "PAY").Select(prefix => $"{prefix}-{Guid.NewGuid():N}")
                from procRef in Gen.Elements("NIBSS", "ISW", "CRD").Select(prefix => $"{prefix}-{Guid.NewGuid():N}")
                from money in MoneyArbitrary().Generator
                from status in Gen.Elements("completed", "pending", "failed", "reversed")
                from dayOffset in Gen.Choose(0, 365)
                from processor in Gen.Elements(ProcessorType.NIBSS, ProcessorType.Interswitch, ProcessorType.Cardify)
                select new PaymentRequest(
                    id,
                    transRef,
                    procRef,
                    money,
                    status,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-dayOffset)),
                    processor))
            .ToArbitrary();
    }

    /// <summary>
    /// Registers all reconciliation-related arbitraries with FsCheck.
    /// </summary>
    public class ReconciliationArbitraries
    {
        public static Arbitrary<SettlementLineItem> SettlementLineItems() => SettlementLineItemArbitrary();
        public static Arbitrary<PaymentRequest> PaymentRequests() => PaymentRequestArbitrary();
        public static Arbitrary<Money> Moneys() => MoneyArbitrary();
    }
}

/// <summary>
/// Placeholder record for SettlementLineItem used in property-based tests.
/// Will be replaced with the actual domain entity once task 2.1 completes.
/// </summary>
public record SettlementLineItem(
    Guid Id,
    Guid BatchId,
    string TransactionReference,
    string ProcessorReference,
    Money Amount,
    string Status,
    DateOnly TransactionDate,
    MatchStatus MatchStatus);

/// <summary>
/// Placeholder record for PaymentRequest used in property-based tests.
/// Represents an internal payment record for reconciliation matching.
/// </summary>
public record PaymentRequest(
    Guid Id,
    string TransactionReference,
    string ProcessorReference,
    Money Amount,
    string Status,
    DateOnly TransactionDate,
    ProcessorType Processor);
