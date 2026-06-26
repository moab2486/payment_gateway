using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Domain.PlatformServices.Reconciliation;
using CardManagement.Domain.ValueObjects;

namespace CardManagement.Infrastructure.PlatformServices.Reconciliation;

/// <summary>
/// Parses Interswitch settlement CSV files.
/// 
/// Expected Interswitch CSV format:
///   TransactionRef,ProcessorRef,Amount,Currency,Status,TransactionDate
///
/// Amount is expressed in the smallest currency unit (e.g., kobo for NGN).
/// TransactionDate format: yyyy-MM-dd.
/// </summary>
public class InterswitchSettlementFileParser
{
    private const int ExpectedFieldCount = 6;

    public async Task<ParseResult> ParseAsync(Stream fileStream, CancellationToken ct)
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
                LineItems = Array.Empty<SettlementLineItem>(),
                TotalRows = 0,
                ParsedRows = 0,
                ErrorRows = 0,
                Errors = Array.Empty<ParseError>()
            };
        }

        // Skip header row
        var dataLines = lines.Skip(1).ToList();
        var parsedItems = new List<SettlementLineItem>();
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

    private static SettlementLineItem ParseLine(string line, Guid batchId)
    {
        var fields = line.Split(',');

        if (fields.Length < ExpectedFieldCount)
            throw new FormatException(
                $"Expected at least {ExpectedFieldCount} fields, got {fields.Length}");

        var transRef = fields[0].Trim();
        var procRef = fields[1].Trim();
        var amountStr = fields[2].Trim();
        var currency = fields[3].Trim();
        var status = fields[4].Trim();
        var dateStr = fields[5].Trim();

        if (string.IsNullOrWhiteSpace(transRef))
            throw new FormatException("Transaction reference is empty.");

        if (string.IsNullOrWhiteSpace(procRef))
            throw new FormatException("Processor reference is empty.");

        if (!long.TryParse(amountStr, out var amount) || amount < 0)
            throw new FormatException($"Invalid amount: '{amountStr}'. Must be a non-negative integer.");

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(char.IsLetter))
            throw new FormatException($"Invalid currency code: '{currency}'. Must be a 3-letter ISO 4217 code.");

        if (string.IsNullOrWhiteSpace(status))
            throw new FormatException("Status is empty.");

        if (!DateOnly.TryParse(dateStr, out var date))
            throw new FormatException($"Invalid date: '{dateStr}'. Expected format: yyyy-MM-dd.");

        var money = new Money(amount, currency);
        return SettlementLineItem.Create(batchId, transRef, procRef, money, status, date);
    }
}
