using CardManagement.Application.PlatformServices.DeveloperPortal;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for PciRedactionFilter.
/// Validates: Requirements 17.1, 17.5
/// </summary>
public class PciRedactionFilterTests
{
    #region PAN Masking Tests

    [Theory]
    [InlineData("4111111111111111", "************1111")] // 16-digit Visa - 12 asterisks + last 4
    [InlineData("5500000000000004", "************0004")]
    [InlineData("1234567890123", "*********0123")] // 13-digit PAN - 9 asterisks + last 4
    [InlineData("1234567890123456789", "***************6789")] // 19-digit PAN - 15 asterisks + last 4
    public void Redact_MasksCardNumbers_InJsonBody(string pan, string expectedMasked)
    {
        var body = $"{{\"cardNumber\":\"{pan}\",\"amount\":5000}}";

        var result = PciRedactionFilter.Redact(body);

        Assert.DoesNotContain(pan, result);
        Assert.Contains(expectedMasked, result);
    }

    [Fact]
    public void Redact_MasksPan_InNestedJson()
    {
        var body = """{"payment":{"card":{"number":"4111111111111111"},"amount":1000}}""";

        var result = PciRedactionFilter.Redact(body);

        Assert.DoesNotContain("4111111111111111", result);
        Assert.Contains("************1111", result);
    }

    [Fact]
    public void Redact_DoesNotMask_ShortDigitSequences()
    {
        var body = """{"amount":5000,"referenceId":"12345"}""";

        var result = PciRedactionFilter.Redact(body);

        // 5 digits and below should not be masked as PANs
        Assert.Equal(body, result);
    }

    #endregion

    #region CVV Masking Tests

    [Theory]
    [InlineData("cvv", "123")]
    [InlineData("cvc", "456")]
    [InlineData("cvv2", "7890")]
    [InlineData("cvc2", "321")]
    [InlineData("securityCode", "999")]
    [InlineData("security_code", "1234")]
    public void Redact_MasksCvvFields(string fieldName, string cvvValue)
    {
        var body = $"{{\"{fieldName}\":\"{cvvValue}\",\"amount\":5000}}";

        var result = PciRedactionFilter.Redact(body);

        Assert.DoesNotContain($"\"{fieldName}\":\"{cvvValue}\"", result);
        Assert.Contains($"\"{fieldName}\":\"***\"", result);
    }

    [Fact]
    public void Redact_MasksCvv_CaseInsensitive()
    {
        var body = """{"CVV":"123","amount":5000}""";

        var result = PciRedactionFilter.Redact(body);

        Assert.Contains("\"***\"", result);
        Assert.DoesNotContain("\"123\"", result);
    }

    #endregion

    #region PIN Masking Tests

    [Theory]
    [InlineData("pin", "1234")]
    [InlineData("pinBlock", "0516001234567890")]
    [InlineData("pin_block", "ABCDEF1234567890")]
    public void Redact_MasksPinFields(string fieldName, string pinValue)
    {
        var body = $"{{\"{fieldName}\":\"{pinValue}\",\"amount\":5000}}";

        var result = PciRedactionFilter.Redact(body);

        Assert.DoesNotContain($"\"{fieldName}\":\"{pinValue}\"", result);
        Assert.Contains($"\"{fieldName}\":\"***\"", result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Redact_ReturnsEmptyString_ForNullInput()
    {
        var result = PciRedactionFilter.Redact(null);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Redact_ReturnsEmptyString_ForEmptyInput()
    {
        var result = PciRedactionFilter.Redact(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Redact_LeavesNonSensitiveData_Unchanged()
    {
        var body = """{"amount":5000,"currency":"NGN","reference":"ref-123","merchant":"shop-xyz"}""";

        var result = PciRedactionFilter.Redact(body);

        Assert.Equal(body, result);
    }

    [Fact]
    public void Redact_MasksMultipleSensitiveFields_InSameBody()
    {
        var body = """{"cardNumber":"4111111111111111","cvv":"123","pin":"4567","amount":5000}""";

        var result = PciRedactionFilter.Redact(body);

        Assert.DoesNotContain("4111111111111111", result);
        Assert.DoesNotContain("\"cvv\":\"123\"", result);
        Assert.DoesNotContain("\"pin\":\"4567\"", result);
        Assert.Contains("************1111", result);
        Assert.Contains("\"cvv\":\"***\"", result);
        Assert.Contains("\"pin\":\"***\"", result);
        Assert.Contains("\"amount\":5000", result);
    }

    #endregion
}
