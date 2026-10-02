namespace PaymentRequest.Api;

public sealed record PaymentInput(string MerchantReference, decimal Amount, string Currency);

public static class PaymentRules
{
    public static string? Validate(PaymentInput input)
    {
        if (string.IsNullOrWhiteSpace(input.MerchantReference) ||
            input.MerchantReference.Trim().Length > 80)
            return "MerchantReference must contain 1 to 80 characters.";
        if (input.Amount <= 0m || input.Amount > 100000m)
            return "Amount must be greater than zero and at most 100000.";
        if (decimal.Round(input.Amount, 2) != input.Amount)
            return "Amount must have at most two decimal places.";
        if (!string.Equals(input.Currency, "INR", StringComparison.OrdinalIgnoreCase))
            return "This practice API supports INR only.";
        return null;
    }

    public static PaymentInput Normalize(PaymentInput input) =>
        input with { MerchantReference = input.MerchantReference.Trim(), Currency = "INR" };

    public static bool SamePayload(PaymentRecord existing, PaymentInput input) =>
        existing.MerchantReference == input.MerchantReference &&
        existing.Amount == input.Amount && existing.Currency == input.Currency;
}
