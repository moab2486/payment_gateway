using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.DTOs;

/// <summary>
/// Result of parsing a settlement file into line items.
/// </summary>
public record ParseResult
{
    /// <summary>
    /// Successfully parsed settlement line items.
    /// </summary>
    public required IReadOnlyList<SettlementLineItem> LineItems { get; init; }

    /// <summary>
    /// Total number of rows in the source file.
    /// </summary>
    public required int TotalRows { get; init; }

    /// <summary>
    /// Number of rows that were successfully parsed.
    /// </summary>
    public required int ParsedRows { get; init; }

    /// <summary>
    /// Number of rows that failed to parse.
    /// </summary>
    public required int ErrorRows { get; init; }

    /// <summary>
    /// Error details for rows that failed parsing.
    /// </summary>
    public required IReadOnlyList<ParseError> Errors { get; init; }
}

/// <summary>
/// Details of a single parse error within a settlement file.
/// </summary>
public record ParseError(int RowNumber, string ErrorMessage);
