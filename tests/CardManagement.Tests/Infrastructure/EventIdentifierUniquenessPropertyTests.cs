using CardManagement.Application.DTOs;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for Event Identifier Uniqueness (Property 4).
/// Validates: Requirements 5.7
/// </summary>
[Trait("Feature", "docker-kafka-integration")]
[Trait("Property", "4")]
public class EventIdentifierUniquenessPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 5.7**
    /// 
    /// Property 4: For any set of N domain events (N=2..100), all assigned EventId values
    /// SHALL be distinct UUIDs — no two events SHALL share the same EventId.
    /// Additionally, no EventId SHALL be Guid.Empty.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AllEventIds_AreDistinctAndNonEmpty()
    {
        var gen = from n in Gen.Choose(2, 100)
                  select n;

        return Prop.ForAll(gen.ToArbitrary(), n =>
        {
            var envelopes = Enumerable.Range(0, n)
                .Select(_ => new EventEnvelope())
                .ToList();

            var eventIds = envelopes.Select(e => e.EventId).ToList();
            var distinctCount = eventIds.Distinct().Count();
            var noneEmpty = eventIds.All(id => id != Guid.Empty);

            return (distinctCount == n && noneEmpty)
                .Label($"Expected {n} distinct non-empty GUIDs, got {distinctCount} distinct (noneEmpty={noneEmpty})");
        });
    }
}
