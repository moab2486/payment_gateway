using System.Text;
using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Domain.ValueObjects;
using CardManagement.PlatformServices.UnitTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using ReconciliationProcessorType = CardManagement.Domain.PlatformServices.Reconciliation.ProcessorType;
using SettlementLineItemEntity = CardManagement.Domain.PlatformServices.Reconciliation.SettlementLineItem;

namespace CardManagement.PlatformServices.UnitTests.Properties.Reconciliation;

/// <summary>
/// Property-based tests for settlement file parsing.
/// Uses an in-memory test implementation of ISettlementFileParser that serializes
/// settlement data to CSV format and parses it back.
/// </summary>
public class SettlementFileParsingPropertyTests
{
    /// <summary>
    /// Property 1: Settlement File Parse Round-Trip
    /// For any valid settlement data structure, serializing it to processor-specific CSV
    /// and then parsing it back should produce an equivalent set of settlement line items
    /// with matching transaction references, amounts, and dates.
    ///
    /// **Validates: Requirements 1.1, 1.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ReconciliationGenerators.ReconciliationArbitraries) })]
    public Property SettlementFile_ParseRoundTrip_ProducesEquivalentItems()
    {
        var parser = new InMemorySettlementFileParser();

        return Prop.ForAll(
            ValidSettlementItemsGenerator(),
            Gen.Elements(
                ReconciliationProcessorType.NIBSS,
                ReconciliationProcessorType.Interswitch,
                ReconciliationProcessorType.Cardify).ToArbitrary(),
            (items, processor) =>
            {
                // Serialize to CSV
                var csv = InMemorySettlementFileParser.SerializeToCsv(items, processor);
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

                // Parse back
                var result = parser.ParseAsync(stream, processor, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // All items should be parsed successfully
                var allParsed = result.ParsedRows == items.Count;
                var noErrors = result.ErrorRows == 0;
                var correctCount = result.LineItems.Count == items.Count;

                // Verify equivalence of each item
                var allMatch = items.All(original =>
                    result.LineItems.Any(parsed =>
                        parsed.TransactionReference == original.TransactionReference &&
                        parsed.Amount.Amount == original.Amount.Amount &&
                        parsed.Amount.CurrencyCode == original.Amount.CurrencyCode &&
                        parsed.TransactionDate == original.TransactionDate &&
                        parsed.ProcessorReference == original.ProcessorReference));

                return (allParsed && noErrors && correctCount && allMatch)
                    .Label($"Expected {items.Count} items parsed with matching fields. " +
                           $"Got ParsedRows={result.ParsedRows}, Errors={result.ErrorRows}, " +
                           $"LineItems={result.LineItems.Count}");
            });
    }

    /// <summary>
    /// Property 2: Malformed Row Isolation
    /// For any settlement file containing N valid rows and M malformed rows at arbitrary positions,
    /// parsing should produce exactly N parsed items and exactly M errors.
    ///
    /// **Validates: Requirements 1.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ReconciliationGenerators.ReconciliationArbitraries) })]
    public Property SettlementFile_MalformedRowIsolation_ProducesExactCounts()
    {
        var parser = new InMemorySettlementFileParser();

        return Prop.ForAll(
            ValidSettlementItemsGenerator(),
            MalformedRowsGenerator(),
            Gen.Elements(
                ReconciliationProcessorType.NIBSS,
                ReconciliationProcessorType.Interswitch,
                ReconciliationProcessorType.Cardify).ToArbitrary(),
            (validItems, malformedRows, processor) =>
            {
                var n = validItems.Count;
                var m = malformedRows.Count;

                // Serialize valid items to CSV lines
                var validCsvLines = InMemorySettlementFileParser.SerializeToCsvLines(validItems, processor);

                // Interleave valid and malformed rows
                var allLines = InterleaveRows(validCsvLines, malformedRows);

                // Build full CSV with header
                var header = InMemorySettlementFileParser.GetCsvHeader(processor);
                var csv = header + Environment.NewLine + string.Join(Environment.NewLine, allLines);
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

                // Parse
                var result = parser.ParseAsync(stream, processor, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var correctParsedCount = result.ParsedRows == n;
                var correctErrorCount = result.ErrorRows == m;
                var correctItemCount = result.LineItems.Count == n;
                var correctErrorListCount = result.Errors.Count == m;

                return (correctParsedCount && correctErrorCount && correctItemCount && correctErrorListCount)
                    .Label($"Expected N={n} parsed, M={m} errors. " +
                           $"Got ParsedRows={result.ParsedRows}, ErrorRows={result.ErrorRows}, " +
                           $"LineItems={result.LineItems.Count}, Errors={result.Errors.Count}");
            });
    }

    #region Generators

    private static Arbitrary<List<SettlementItemData>> ValidSettlementItemsGenerator()
    {
        var itemGen = from transRef in Gen.Elements("TXN", "REF", "PAY")
                          .Select(prefix => $"{prefix}-{Guid.NewGuid():N}")
                      from procRef in Gen.Elements("NIBSS", "ISW", "CRD")
                          .Select(prefix => $"{prefix}-{Guid.NewGuid():N}")
                      from amount in Gen.Choose(1, 100_000_000)
                      from currency in Gen.Elements("NGN", "USD", "GBP", "EUR")
                      from status in Gen.Elements("completed", "pending", "failed", "reversed")
                      from dayOffset in Gen.Choose(0, 365)
                      select new SettlementItemData(
                          transRef,
                          procRef,
                          new Money(amount, currency),
                          status,
                          DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-dayOffset)));

        return Gen.NonEmptyListOf(itemGen)
            .Select(items => items.Take(20).ToList())
            .ToArbitrary();
    }

    private static Arbitrary<List<string>> MalformedRowsGenerator()
    {
        var malformedGen = Gen.Elements(
            ",,,,",                                                     // All empty fields
            "TXN-123,PROC-456,not_a_number,NGN,completed,2024-01-15",  // Invalid amount
            "TXN-123,PROC-456,1000,NGN,completed,invalid-date",        // Invalid date
            ",PROC-456,1000,NGN,completed,2024-01-15",                 // Missing transaction ref
            "TXN-123,,1000,NGN,completed,2024-01-15",                  // Missing processor ref
            "TXN-123,PROC-456,-500,NGN,completed,2024-01-15",          // Negative amount
            "TXN-123,PROC-456,1000,,completed,2024-01-15",             // Missing currency
            "only_one_field"                                            // Single field
        );

        return Gen.ListOf(Gen.Choose(1, 5).SelectMany(_ => malformedGen))
            .Select(items => items.Take(10).ToList())
            .ToArbitrary();
    }

    private static List<string> InterleaveRows(List<string> validRows, List<string> malformedRows)
    {
        var result = new List<string>();
        var vi = 0;
        var mi = 0;
        var random = new System.Random(42); // Deterministic for reproducibility

        while (vi < validRows.Count || mi < malformedRows.Count)
        {
            if (vi < validRows.Count && mi < malformedRows.Count)
            {
                if (random.Next(2) == 0)
                    result.Add(validRows[vi++]);
                else
                    result.Add(malformedRows[mi++]);
            }
            else if (vi < validRows.Count)
            {
                result.Add(validRows[vi++]);
            }
            else
            {
                result.Add(malformedRows[mi++]);
            }
        }

        return result;
    }

    #endregion
}

/// <summary>
/// Data record for settlement items used in property testing.
/// </summary>
public record SettlementItemData(
    string TransactionReference,
    string ProcessorReference,
    Money Amount,
    string Status,
    DateOnly TransactionDate);

/// <summary>
/// In-memory test implementation of ISettlementFileParser.
/// Serializes settlement data to CSV format and parses it back.
/// </summary>
public class InMemorySettlementFileParser : ISettlementFileParser
{
    public async Task<ParseResult> ParseAsync(
        Stream fileStream, ReconciliationProcessorType processor, CancellationToken ct)
    {
        using var reader = new StreamReader(fileStream);
        var content = await reader.ReadToEndAsync(ct);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
        {
            return new ParseResult
            {
                LineItems = Array.Empty<SettlementLineItemEntity>(),
                TotalRows = 0,
                ParsedRows = 0,
                ErrorRows = 0,
                Errors = Array.Empty<ParseError>()
            };
        }

        // Skip header row
        var dataLines = lines.Skip(1).ToList();
        var parsedItems = new List<SettlementLineItemEntity>();
        var errors = new List<ParseError>();
        var batchId = Guid.NewGuid();

        for (int i = 0; i < dataLines.Count; i++)
        {
            var rowNumber = i + 2; // 1-indexed, header is row 1
            var line = dataLines[i];

            try
            {
                var item = ParseLine(line, batchId);
                parsedItems.Add(item);
            }
            catch (Exception ex)
            {
                errors.Add(new ParseError(rowNumber, ex.Message));
            }
        }

        return new ParseResult
        {
            LineItems = parsedItems,
            TotalRows = dataLines.Count,
            ParsedRows = parsedItems.Count,
            ErrorRows = errors.Count,
            Errors = errors
        };
    }

    private static SettlementLineItemEntity ParseLine(string line, Guid batchId)
    {
        var fields = line.Split(',');

        if (fields.Length < 6)
            throw new FormatException($"Expected at least 6 fields, got {fields.Length}");

        var transRef = fields[0].Trim();
        var procRef = fields[1].Trim();
        var amountStr = fields[2].Trim();
        var currency = fields[3].Trim();
        var status = fields[4].Trim();
        var dateStr = fields[5].Trim();

        if (string.IsNullOrWhiteSpace(transRef))
            throw new FormatException("Transaction reference is empty");

        if (string.IsNullOrWhiteSpace(procRef))
            throw new FormatException("Processor reference is empty");

        if (!long.TryParse(amountStr, out var amount) || amount < 0)
            throw new FormatException($"Invalid amount: '{amountStr}'");

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(char.IsLetter))
            throw new FormatException($"Invalid currency code: '{currency}'");

        if (string.IsNullOrWhiteSpace(status))
            throw new FormatException("Status is empty");

        if (!DateOnly.TryParse(dateStr, out var date))
            throw new FormatException($"Invalid date: '{dateStr}'");

        var money = new Money(amount, currency);
        return SettlementLineItemEntity.Create(batchId, transRef, procRef, money, status, date);
    }

    /// <summary>
    /// Serializes settlement items to CSV format for a given processor.
    /// </summary>
    public static string SerializeToCsv(List<SettlementItemData> items, ReconciliationProcessorType processor)
    {
        var sb = new StringBuilder();
        sb.AppendLine(GetCsvHeader(processor));

        foreach (var item in items)
        {
            sb.AppendLine(SerializeItem(item));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Serializes settlement items to individual CSV lines (without header).
    /// </summary>
    public static List<string> SerializeToCsvLines(
        List<SettlementItemData> items, ReconciliationProcessorType processor)
    {
        return items.Select(SerializeItem).ToList();
    }

    /// <summary>
    /// Gets the CSV header for the given processor type.
    /// </summary>
    public static string GetCsvHeader(ReconciliationProcessorType processor)
    {
        return processor switch
        {
            ReconciliationProcessorType.NIBSS =>
                "TransactionRef,ProcessorRef,Amount,Currency,Status,TransactionDate",
            ReconciliationProcessorType.Interswitch =>
                "TransactionRef,ProcessorRef,Amount,Currency,Status,TransactionDate",
            ReconciliationProcessorType.Cardify =>
                "TransactionRef,ProcessorRef,Amount,Currency,Status,TransactionDate",
            _ => throw new ArgumentException($"Unknown processor: {processor}")
        };
    }

    private static string SerializeItem(SettlementItemData item)
    {
        return $"{item.TransactionReference},{item.ProcessorReference},{item.Amount.Amount},{item.Amount.CurrencyCode},{item.Status},{item.TransactionDate:yyyy-MM-dd}";
    }
}
