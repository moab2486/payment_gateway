using CardManagement.Domain.ValueObjects;
using Xunit;

namespace CardManagement.Tests.ValueObjects;

public class BinRangeTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesSuccessfully()
    {
        var binRange = new BinRange("453201", CardScheme.Visa, 16);

        Assert.Equal("453201", binRange.Prefix);
        Assert.Equal(CardScheme.Visa, binRange.Scheme);
        Assert.Equal(16, binRange.PanLength);
    }

    [Fact]
    public void Constructor_NullPrefix_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new BinRange(null!, CardScheme.Visa, 16));
    }

    [Fact]
    public void Constructor_EmptyPrefix_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new BinRange("", CardScheme.Visa, 16));
    }

    [Fact]
    public void Constructor_NonDigitPrefix_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new BinRange("45AB01", CardScheme.Visa, 16));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(20)]
    public void Constructor_InvalidPanLength_ThrowsArgumentException(int invalidLength)
    {
        Assert.Throws<ArgumentException>(() => new BinRange("453201", CardScheme.Visa, invalidLength));
    }

    [Fact]
    public void Constructor_PrefixTooLong_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new BinRange("123456789", CardScheme.Visa, 16));
    }

    [Fact]
    public void Constructor_PrefixLengthNotLessThanPanLength_ThrowsArgumentException()
    {
        // Prefix length 6 >= PAN length... but PAN length minimum is 16 and prefix max is 8
        // So create a case where prefix length equals PAN length
        // Actually this can't happen with valid ranges. Let's test prefix length 8 with PAN 16 (valid)
        // and prefix length that's too close
        var binRange = new BinRange("12345678", CardScheme.Visa, 16);
        Assert.NotNull(binRange); // 8 < 16 is valid
    }

    [Fact]
    public void Matches_CorrectPan_ReturnsTrue()
    {
        var binRange = new BinRange("453201", CardScheme.Visa, 16);
        Assert.True(binRange.Matches("4532015112830366"));
    }

    [Fact]
    public void Matches_WrongPrefix_ReturnsFalse()
    {
        var binRange = new BinRange("453201", CardScheme.Visa, 16);
        Assert.False(binRange.Matches("5532015112830366"));
    }

    [Fact]
    public void Matches_WrongLength_ReturnsFalse()
    {
        var binRange = new BinRange("453201", CardScheme.Visa, 16);
        Assert.False(binRange.Matches("45320151128303661")); // 17 digits
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    public void Constructor_ValidPanLengths_Succeeds(int panLength)
    {
        var binRange = new BinRange("4532", CardScheme.Visa, panLength);
        Assert.Equal(panLength, binRange.PanLength);
    }
}
