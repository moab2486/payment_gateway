using CardManagement.Domain.ValueObjects;

namespace CardManagement.Domain.Services;

/// <summary>
/// Domain service that resolves which BIN range a PAN belongs to.
/// Matches the longest prefix first when multiple ranges could match.
/// </summary>
public static class BinRangeResolver
{
    /// <summary>
    /// Finds the matching BIN range for a given PAN by prefix matching.
    /// If multiple ranges match, the one with the longest prefix wins.
    /// </summary>
    /// <param name="pan">The full PAN string to resolve.</param>
    /// <param name="ranges">The list of configured BIN ranges to match against.</param>
    /// <returns>The matching <see cref="BinRange"/>, or null if no range matches.</returns>
    public static BinRange? Resolve(string pan, IReadOnlyList<BinRange> ranges)
    {
        if (string.IsNullOrEmpty(pan) || ranges == null || ranges.Count == 0)
            return null;

        BinRange? bestMatch = null;
        int longestPrefix = 0;

        for (int i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];

            if (range.Matches(pan) && range.Prefix.Length > longestPrefix)
            {
                bestMatch = range;
                longestPrefix = range.Prefix.Length;
            }
        }

        return bestMatch;
    }
}
