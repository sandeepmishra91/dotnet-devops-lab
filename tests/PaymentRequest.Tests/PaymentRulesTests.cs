using System.Globalization;
using PaymentRequest.Api;
using Xunit;

public class PaymentRulesTests
{
    [Theory]
    [InlineData("0.01")]
    [InlineData("100000")]
    public void AcceptsValidBoundaries(string amount) =>
        Assert.Null(PaymentRules.Validate(new("merchant-1",
            decimal.Parse(amount, CultureInfo.InvariantCulture), "INR")));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100000.01")]
    [InlineData("10.001")]
    public void RejectsInvalidAmount(string amount) =>
        Assert.NotNull(PaymentRules.Validate(new("merchant-1",
            decimal.Parse(amount, CultureInfo.InvariantCulture), "INR")));

    [Fact]
    public void RejectsMissingMerchant() =>
        Assert.NotNull(PaymentRules.Validate(new(" ", 10m, "INR")));

    [Fact]
    public void RejectsUnsupportedCurrency() =>
        Assert.NotNull(PaymentRules.Validate(new("merchant-1", 10m, "USD")));

    [Fact]
    public void NormalizesSupportedInput() =>
        Assert.Equal(new PaymentInput("merchant-1", 10m, "INR"),
            PaymentRules.Normalize(new(" merchant-1 ", 10m, "inr")));

    [Fact]
    public void DetectsChangedPayload()
    {
        var existing = new PaymentRecord { MerchantReference = "merchant-1", Amount = 10m, Currency = "INR" };
        Assert.True(PaymentRules.SamePayload(existing, new("merchant-1", 10m, "INR")));
        Assert.False(PaymentRules.SamePayload(existing, new("merchant-1", 11m, "INR")));
    }
}
