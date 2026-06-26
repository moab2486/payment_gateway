using CardManagement.Domain.ValueObjects;
using Xunit;

namespace CardManagement.Tests.ValueObjects;

public class PanTests
{
    [Theory]
    [InlineData("4532015112830366")]    // Valid Visa 16-digit
    [InlineData("5425233430109903")]    // Valid Mastercard 16-digit
    [InlineData("4532015112830366120")] // Valid 19-digit (passes Luhn)
    public void Constructor_ValidPan_CreatesSuccessfully(string validPan)
    {
        var pan = new Pan(validPan);
        Assert.Equal(validPan, pan.Value);
    }

    [Fact]
    public void Constructor_NullValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Pan(null!));
    }

    [Fact]
    public void Constructor_EmptyValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Pan(""));
    }

    [Fact]
    public void Constructor_WhitespaceValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Pan("   "));
    }

    [Fact]
    public void Constructor_NonDigitCharacters_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Pan("4532015112830A66"));
    }

    [Theory]
    [InlineData("453201511283")]       // 12 digits - too short
    [InlineData("45320151128303")]     // 14 digits - too short
    [InlineData("453201511283036")]    // 15 digits - too short
    public void Constructor_TooShort_ThrowsArgumentException(string shortPan)
    {
        Assert.Throws<ArgumentException>(() => new Pan(shortPan));
    }

    [Fact]
    public void Constructor_TooLong_ThrowsArgumentException()
    {
        // 20 digits
        Assert.Throws<ArgumentException>(() => new Pan("45320151128303661234"));
    }

    [Theory]
    [InlineData("4532015112830367")]    // Last digit changed - fails Luhn
    [InlineData("1234567890123456")]    // Arbitrary - fails Luhn
    public void Constructor_FailsLuhn_ThrowsArgumentException(string invalidLuhn)
    {
        Assert.Throws<ArgumentException>(() => new Pan(invalidLuhn));
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var pan = new Pan("4532015112830366");
        Assert.Equal("4532015112830366", pan.ToString());
    }

    [Fact]
    public void RecordEquality_SameValue_AreEqual()
    {
        var pan1 = new Pan("4532015112830366");
        var pan2 = new Pan("4532015112830366");
        Assert.Equal(pan1, pan2);
    }

    [Fact]
    public void RecordEquality_DifferentValue_AreNotEqual()
    {
        var pan1 = new Pan("4532015112830366");
        var pan2 = new Pan("5425233430109903");
        Assert.NotEqual(pan1, pan2);
    }
}
