using CardManagement.Infrastructure.Pipeline;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for System Trace Audit Number Uniqueness (Property 15).
/// 
/// **Validates: Requirements 10.4**
/// 
/// For any set of N transactions processed by the system (where N > 1), all assigned
/// System Trace Audit Numbers SHALL be distinct — no two transactions SHALL share
/// the same STAN value.
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "15")]
public class StanUniquenessPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 10.4**
    /// 
    /// Property 15: System Trace Audit Number Uniqueness.
    /// Generate batch sizes N between 2 and 100.
    /// Call GenerateStan() N times, collect all results.
    /// Assert: all STANs are distinct (no duplicates in the batch).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GenerateStan_ProducesDistinctValues_ForAnyBatchSize()
    {
        var batchSizeGen = Gen.Choose(2, 100);

        return Prop.ForAll(batchSizeGen.ToArbitrary(), batchSize =>
        {
            // Act: Generate N STANs
            var stans = new List<string>(batchSize);
            for (int i = 0; i < batchSize; i++)
            {
                stans.Add(TransactionPipeline.GenerateStan());
            }

            // Assert: all STANs are distinct
            var distinctCount = stans.Distinct().Count();

            return (distinctCount == batchSize)
                .Label($"Expected {batchSize} distinct STANs but got {distinctCount}. " +
                       $"Duplicates found: [{string.Join(", ", stans.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => $"{g.Key}(x{g.Count()})"))}]");
        });
    }

    /// <summary>
    /// **Validates: Requirements 10.4**
    /// 
    /// Additional verification: STANs are always 6-digit numeric strings.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GenerateStan_AlwaysProduces6DigitNumericString()
    {
        var batchSizeGen = Gen.Choose(2, 100);

        return Prop.ForAll(batchSizeGen.ToArbitrary(), batchSize =>
        {
            var stans = new List<string>(batchSize);
            for (int i = 0; i < batchSize; i++)
            {
                stans.Add(TransactionPipeline.GenerateStan());
            }

            var allValid = stans.All(stan =>
                stan.Length == 6 &&
                stan.All(c => c >= '0' && c <= '9') &&
                int.Parse(stan) >= 1 &&
                int.Parse(stan) <= 999999);

            return allValid
                .Label($"Not all STANs are valid 6-digit numeric strings in range [000001-999999]. " +
                       $"Invalid: [{string.Join(", ", stans.Where(s => s.Length != 6 || !s.All(c => c >= '0' && c <= '9')))}]");
        });
    }
}
