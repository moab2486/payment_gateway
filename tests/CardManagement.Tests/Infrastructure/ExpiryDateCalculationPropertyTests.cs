using CardManagement.Infrastructure.Cards;
using FsCheck;
using FsCheck.Xunit;
using System.Text.RegularExpressions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for Expiry Date Calculation (Property 9).
/// Validates: Requirements 5.3
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "9")]
public class ExpiryDateCalculationPropertyTests
{
    private static readonly Regex MmYyRegex = new(@"^\d{2}/\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// **Validates: Requirements 5.3**
    /// 
    /// Property 9a: For any validity period between 1 and 60 months,
    /// the calculated expiry date SHALL be in MM/YY format (two-digit month, slash, two-digit year).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ExpiryDate_IsInMmYyFormat()
    {
        var gen = Gen.Choose(1, 60);

        return Prop.ForAll(gen.ToArbitrary(), validityMonths =>
        {
            var result = VirtualCardService.CalculateExpiryDate(validityMonths);

            return MmYyRegex.IsMatch(result)
                .Label($"Expected MM/YY format but got '{result}' for validityMonths={validityMonths}");
        });
    }

    /// <summary>
    /// **Validates: Requirements 5.3**
    /// 
    /// Property 9b: For any validity period between 1 and 60 months,
    /// the month component of the expiry date SHALL be between 01 and 12.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ExpiryDate_MonthComponentIsBetween01And12()
    {
        var gen = Gen.Choose(1, 60);

        return Prop.ForAll(gen.ToArbitrary(), validityMonths =>
        {
            var result = VirtualCardService.CalculateExpiryDate(validityMonths);
            var monthStr = result.Substring(0, 2);
            var month = int.Parse(monthStr);

            return (month >= 1 && month <= 12)
                .Label($"Month should be 01-12 but got '{monthStr}' for validityMonths={validityMonths}");
        });
    }

    /// <summary>
    /// **Validates: Requirements 5.3**
    /// 
    /// Property 9c: For any validity period between 1 and 60 months,
    /// the calculated expiry date SHALL represent the correct month offset from the current date.
    /// The month and year should match DateTime.UtcNow.AddMonths(validityMonths).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ExpiryDate_HasCorrectMonthOffset()
    {
        var gen = Gen.Choose(1, 60);

        return Prop.ForAll(gen.ToArbitrary(), validityMonths =>
        {
            // Capture time before and after to handle potential month boundary crossing
            var before = DateTime.UtcNow.AddMonths(validityMonths);
            var result = VirtualCardService.CalculateExpiryDate(validityMonths);
            var after = DateTime.UtcNow.AddMonths(validityMonths);

            var monthStr = result.Substring(0, 2);
            var yearStr = result.Substring(3, 2);
            var month = int.Parse(monthStr);
            var year = int.Parse(yearStr);

            // The result should match either the 'before' or 'after' snapshot
            // to account for the tiny time window between capturing UtcNow
            var matchesBefore = (month == before.Month && year == before.Year % 100);
            var matchesAfter = (month == after.Month && year == after.Year % 100);

            return (matchesBefore || matchesAfter)
                .Label($"Expected month/year to match UtcNow+{validityMonths}months " +
                       $"(expected {before:MM/yy} or {after:MM/yy}) but got '{result}'");
        });
    }
}
