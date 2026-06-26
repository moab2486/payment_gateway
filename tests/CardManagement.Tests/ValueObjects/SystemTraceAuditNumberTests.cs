using CardManagement.Domain.ValueObjects;
using Xunit;

namespace CardManagement.Tests.ValueObjects;

public class SystemTraceAuditNumberTests
{
    [Theory]
    [InlineData("000001")]
    [InlineData("123456")]
    [InlineData("999999")]
    public void Constructor_ValidStan_CreatesSuccessfully(string validStan)
    {
        var stan = new SystemTraceAuditNumber(validStan);
        Assert.Equal(validStan, stan.Value);
    }

    [Fact]
    public void Constructor_NullValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new SystemTraceAuditNumber(null!));
    }

    [Fact]
    public void Constructor_EmptyValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new SystemTraceAuditNumber(""));
    }

    [Theory]
    [InlineData("12345")]     // 5 digits - too short
    [InlineData("1234567")]   // 7 digits - too long
    [InlineData("1")]         // 1 digit
    public void Constructor_WrongLength_ThrowsArgumentException(string invalidStan)
    {
        Assert.Throws<ArgumentException>(() => new SystemTraceAuditNumber(invalidStan));
    }

    [Theory]
    [InlineData("12345A")]    // Contains letter
    [InlineData("ABCDEF")]    // All letters
    [InlineData("12 456")]    // Contains space
    public void Constructor_NonDigitCharacters_ThrowsArgumentException(string invalidStan)
    {
        Assert.Throws<ArgumentException>(() => new SystemTraceAuditNumber(invalidStan));
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var stan = new SystemTraceAuditNumber("123456");
        Assert.Equal("123456", stan.ToString());
    }

    [Fact]
    public void RecordEquality_SameValue_AreEqual()
    {
        var stan1 = new SystemTraceAuditNumber("123456");
        var stan2 = new SystemTraceAuditNumber("123456");
        Assert.Equal(stan1, stan2);
    }

    [Fact]
    public void RecordEquality_DifferentValue_AreNotEqual()
    {
        var stan1 = new SystemTraceAuditNumber("123456");
        var stan2 = new SystemTraceAuditNumber("654321");
        Assert.NotEqual(stan1, stan2);
    }
}
