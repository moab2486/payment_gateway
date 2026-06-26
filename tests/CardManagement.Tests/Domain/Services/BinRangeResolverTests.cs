using CardManagement.Domain.Services;
using CardManagement.Domain.ValueObjects;
using Xunit;

namespace CardManagement.Tests.Services;

public class BinRangeResolverTests
{
    private static readonly List<BinRange> TestRanges = new()
    {
        new BinRange("4111", CardScheme.Visa, 16),
        new BinRange("411111", CardScheme.Visa, 16),
        new BinRange("55", CardScheme.Mastercard, 16),
        new BinRange("506099", CardScheme.Verve, 19),
    };

    [Fact]
    public void Resolve_WithMatchingPan_ReturnsCorrectBinRange()
    {
        // PAN matches the "55" Mastercard prefix
        var pan = "5500000000000004";
        var result = BinRangeResolver.Resolve(pan, TestRanges);

        Assert.NotNull(result);
        Assert.Equal("55", result.Prefix);
        Assert.Equal(CardScheme.Mastercard, result.Scheme);
    }

    [Fact]
    public void Resolve_WithMultipleMatchingPrefixes_ReturnsLongestPrefixMatch()
    {
        // PAN "4111111111111111" matches both "4111" and "411111" prefixes
        var pan = "4111111111111111";
        var result = BinRangeResolver.Resolve(pan, TestRanges);

        Assert.NotNull(result);
        Assert.Equal("411111", result.Prefix);
        Assert.Equal(CardScheme.Visa, result.Scheme);
    }

    [Fact]
    public void Resolve_WithNoMatchingRange_ReturnsNull()
    {
        var pan = "9999999999999999";
        var result = BinRangeResolver.Resolve(pan, TestRanges);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_WithPanLengthMismatch_ReturnsNull()
    {
        // "506099" requires PanLength 19, but this PAN is 16 digits
        var pan = "5060990000000000";
        var result = BinRangeResolver.Resolve(pan, TestRanges);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_WithNullPan_ReturnsNull()
    {
        var result = BinRangeResolver.Resolve(null!, TestRanges);
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_WithEmptyPan_ReturnsNull()
    {
        var result = BinRangeResolver.Resolve("", TestRanges);
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_WithNullRanges_ReturnsNull()
    {
        var result = BinRangeResolver.Resolve("4111111111111111", null!);
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_WithEmptyRanges_ReturnsNull()
    {
        var result = BinRangeResolver.Resolve("4111111111111111", new List<BinRange>());
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_WithVervePan_MatchesVerveRange()
    {
        // 19-digit PAN matching Verve "506099" prefix
        var pan = "5060990000000000000";
        var result = BinRangeResolver.Resolve(pan, TestRanges);

        Assert.NotNull(result);
        Assert.Equal("506099", result.Prefix);
        Assert.Equal(CardScheme.Verve, result.Scheme);
    }
}
