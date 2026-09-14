using EventTicketing.ServiceDefaults.Errors;
using Ticketing.Api.Services;
using Xunit;

namespace Ticketing.UnitTests;

public sealed class PurchaseRuleEdgeCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void Quantity_boundaries_are_accepted(int quantity) =>
        PurchaseRules.Validate(quantity, "buyer@example.com", "request-123",
            Now.AddDays(1), true, Now);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_customer_email_is_rejected(string email) =>
        Assert.Throws<RequestValidationException>(() =>
            PurchaseRules.Validate(1, email, "request-123", Now.AddDays(1), true, Now));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_idempotency_key_is_rejected(string key) =>
        Assert.Throws<RequestValidationException>(() =>
            PurchaseRules.Validate(1, "buyer@example.com", key, Now.AddDays(1), true, Now));

    [Fact]
    public void Idempotency_key_longer_than_100_characters_is_rejected() =>
        Assert.Throws<RequestValidationException>(() =>
            PurchaseRules.Validate(1, "buyer@example.com", new string('x', 101),
                Now.AddDays(1), true, Now));
}
