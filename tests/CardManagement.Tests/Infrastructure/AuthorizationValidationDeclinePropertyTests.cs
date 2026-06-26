using CardManagement.Application.DTOs;
using CardManagement.Infrastructure.Iso8583;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for Authorization Validation Decline (Property 4).
/// Validates: Requirements 3.5, 4.5
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "4")]
public class AuthorizationValidationDeclinePropertyTests
{
    /// <summary>
    /// Mandatory field numbers for authorization requests (MTI 0100).
    /// </summary>
    private static readonly int[] MandatoryFieldNumbers = { 2, 3, 4, 11, 14, 22, 25, 41, 49 };

    /// <summary>
    /// Valid values for each mandatory field, conforming to the validator's structural rules.
    /// </summary>
    private static readonly Dictionary<int, string> ValidFieldValues = new()
    {
        [2] = "4532015112830366",   // PAN: 16 digits
        [3] = "000000",             // Processing Code: 6 digits
        [4] = "000000010000",       // Amount: up to 12 digits
        [11] = "123456",            // STAN: 6 digits
        [14] = "2512",              // Expiry Date: 4 digits (YYMM)
        [22] = "051",               // POS Entry Mode: 3 digits
        [25] = "00",                // POS Condition Code: 2 digits
        [41] = "TERM0001",          // Terminal ID: 8 characters
        [49] = "566"                // Currency Code: 3 characters
    };

    private static AuthorizationMessageValidator CreateValidator()
    {
        return new AuthorizationMessageValidator(
            NullLogger<AuthorizationMessageValidator>.Instance);
    }

    /// <summary>
    /// **Validates: Requirements 3.5, 4.5**
    /// 
    /// Property 4: For any ISO 8583 authorization request message (MTI 0100) that has one
    /// or more mandatory fields randomly removed, the validator SHALL return a failure result
    /// with IsSuccess=false and ErrorCode="30" (format error).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AuthorizationMessage_WithRemovedMandatoryFields_ReturnsFormatError()
    {
        // Generator: pick a random non-empty subset of mandatory fields to remove
        var gen = from fieldsToRemove in GenNonEmptySubset(MandatoryFieldNumbers)
                  select fieldsToRemove;

        return Prop.ForAll(gen.ToArbitrary(), fieldsToRemove =>
        {
            // Build a message with all mandatory fields present, then remove the selected subset
            var fields = new Dictionary<int, string>(ValidFieldValues);
            foreach (var fieldNumber in fieldsToRemove)
            {
                fields.Remove(fieldNumber);
            }

            var message = new Iso8583Message
            {
                Mti = "0100",
                Fields = fields
            };

            var validator = CreateValidator();
            var result = validator.Validate(message);

            var removedFieldsList = string.Join(", ", fieldsToRemove.OrderBy(f => f));

            return (!result.IsSuccess)
                .Label($"Expected IsSuccess=false when fields [{removedFieldsList}] are removed, but got IsSuccess=true")
                .And(() => (result.ErrorCode == "30")
                    .Label($"Expected ErrorCode='30' when fields [{removedFieldsList}] are removed, but got '{result.ErrorCode}'"));
        });
    }

    /// <summary>
    /// Generates a non-empty subset (1 or more elements) of the given array.
    /// Each element is independently included with 50% probability, ensuring at least one is included.
    /// </summary>
    private static Gen<int[]> GenNonEmptySubset(int[] items)
    {
        // Generate a boolean mask for each item (true = include in subset)
        var maskGen = Gen.ArrayOf(items.Length, Gen.Elements(true, false));

        return maskGen
            .Select(mask =>
            {
                var subset = items
                    .Where((item, index) => mask[index])
                    .ToArray();
                return subset;
            })
            .Where(subset => subset.Length > 0); // Ensure non-empty
    }
}
