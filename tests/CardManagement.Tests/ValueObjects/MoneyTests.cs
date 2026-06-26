using CardManagement.Domain.ValueObjects;
using Xunit;

namespace CardManagement.Tests.ValueObjects;

public class MoneyTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesSuccessfully()
    {
        var money = new Money(10000, "NGN");

        Assert.Equal(10000, money.Amount);
        Assert.Equal("NGN", money.CurrencyCode);
    }

    [Fact]
    public void Constructor_ZeroAmount_CreatesSuccessfully()
    {
        var money = new Money(0, "USD");
        Assert.Equal(0, money.Amount);
    }

    [Fact]
    public void Constructor_NegativeAmount_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Money(-1, "USD"));
    }

    [Fact]
    public void Constructor_NullCurrencyCode_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Money(100, null!));
    }

    [Fact]
    public void Constructor_EmptyCurrencyCode_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Money(100, ""));
    }

    [Theory]
    [InlineData("US")]     // Too short
    [InlineData("USDX")]   // Too long
    [InlineData("12D")]    // Contains digits
    [InlineData("U$D")]    // Contains special chars
    public void Constructor_InvalidCurrencyCode_ThrowsArgumentException(string invalidCode)
    {
        Assert.Throws<ArgumentException>(() => new Money(100, invalidCode));
    }

    [Fact]
    public void Constructor_LowercaseCurrencyCode_ConvertsToUppercase()
    {
        var money = new Money(100, "ngn");
        Assert.Equal("NGN", money.CurrencyCode);
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        var money = new Money(10000, "NGN");
        Assert.Equal("10000 NGN", money.ToString());
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var money1 = new Money(5000, "USD");
        var money2 = new Money(5000, "USD");
        Assert.Equal(money1, money2);
    }

    [Fact]
    public void RecordEquality_DifferentAmount_AreNotEqual()
    {
        var money1 = new Money(5000, "USD");
        var money2 = new Money(6000, "USD");
        Assert.NotEqual(money1, money2);
    }

    [Fact]
    public void RecordEquality_DifferentCurrency_AreNotEqual()
    {
        var money1 = new Money(5000, "USD");
        var money2 = new Money(5000, "NGN");
        Assert.NotEqual(money1, money2);
    }

    [Fact]
    public void Constructor_LargeAmount_CreatesSuccessfully()
    {
        var money = new Money(long.MaxValue, "USD");
        Assert.Equal(long.MaxValue, money.Amount);
    }
}
