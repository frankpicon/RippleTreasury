using EventTicketing.ServiceDefaults.Errors;
using Ticketing.Api.Services;
using Xunit;

namespace Ticketing.UnitTests;

public sealed class PurchaseRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Active_future_event_accepts_valid_purchase() =>
        PurchaseRules.Validate(2, "buyer@example.com", "request-123", Now.AddDays(1), true, Now);

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Quantity_outside_purchase_limit_is_rejected(int quantity) =>
        Assert.Throws<RequestValidationException>(() =>
            PurchaseRules.Validate(quantity, "buyer@example.com", "request-123",
                Now.AddDays(1), true, Now));

    [Fact]
    public void Inactive_event_is_rejected() =>
        Assert.Throws<ResourceConflictException>(() =>
            PurchaseRules.Validate(1, "buyer@example.com", "request-123",
                Now.AddDays(1), false, Now));

    [Fact]
    public void Started_event_is_rejected() =>
        Assert.Throws<ResourceConflictException>(() =>
            PurchaseRules.Validate(1, "buyer@example.com", "request-123", Now, true, Now));
}
