using CardManagement.Domain.Services;
using Xunit;

namespace CardManagement.Tests.Services;

public class LuhnValidatorTests
{
    [Theory]
    [InlineData("4539148803436467")] // Valid Visa
    [InlineData("5500000000000004")] // Valid Mastercard
    [InlineData("4111111111111111")] // Valid Visa
    [InlineData("79927398713")]      // Classic Luhn example
    public void IsValid_WithValidPan_ReturnsTrue(string pan)
    {
        Assert.True(LuhnValidator.IsValid(pan));
    }

    [Theory]
    [InlineData("4539148803436468")] // One digit off
    [InlineData("1234567890123456")] // Random digits
    [InlineData("0000000000000001")] // Fails Luhn
    public void IsValid_WithInvalidPan_ReturnsFalse(string pan)
    {
        Assert.False(LuhnValidator.IsValid(pan));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12345abc6789")]
    public void IsValid_WithInvalidInput_ReturnsFalse(string? pan)
    {
        Assert.False(LuhnValidator.IsValid(pan!));
    }

    [Theory]
    [InlineData("453914880343646", '7')]  // Full PAN: 4539148803436467
    [InlineData("411111111111111", '1')]  // Full PAN: 4111111111111111
    [InlineData("550000000000000", '4')]  // Full PAN: 5500000000000004
    [InlineData("7992739871", '3')]       // Full PAN: 79927398713
    public void ComputeCheckDigit_ReturnsCorrectDigit(string partialPan, char expectedCheckDigit)
    {
        var result = LuhnValidator.ComputeCheckDigit(partialPan);
        Assert.Equal(expectedCheckDigit, result);
    }

    [Fact]
    public void ComputeCheckDigit_ResultMakesFullPanValid()
    {
        var partialPan = "453914880343646";
        var checkDigit = LuhnValidator.ComputeCheckDigit(partialPan);
        var fullPan = partialPan + checkDigit;

        Assert.True(LuhnValidator.IsValid(fullPan));
    }

    [Fact]
    public void ComputeCheckDigit_WithNullInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LuhnValidator.ComputeCheckDigit(null!));
    }

    [Fact]
    public void ComputeCheckDigit_WithEmptyInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LuhnValidator.ComputeCheckDigit(""));
    }

    [Fact]
    public void ComputeCheckDigit_WithNonDigitInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LuhnValidator.ComputeCheckDigit("12345abc"));
    }
}
