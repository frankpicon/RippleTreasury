using System.ComponentModel.DataAnnotations;
using Ticketing.Api.Contracts;
using Xunit;

namespace Ticketing.UnitTests;

public sealed class PurchaseRequestContractTests
{
    [Theory]
    [InlineData("not-an-email")]
    [InlineData("buyer@")]
    public void Invalid_customer_email_fails_contract_validation(string email)
    {
        var request = new PurchaseTicketsRequest
        {
            PricingTierId = Guid.NewGuid(),
            ExpectedUnitPrice = 50,
            CustomerEmail = email,
            Quantity = 1
        };

        Assert.False(Validator.TryValidateObject(
            request, new ValidationContext(request), [], validateAllProperties: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Invalid_quantity_fails_contract_validation(int quantity)
    {
        var request = new PurchaseTicketsRequest
        {
            PricingTierId = Guid.NewGuid(),
            ExpectedUnitPrice = 50,
            CustomerEmail = "buyer@example.com",
            Quantity = quantity
        };

        Assert.False(Validator.TryValidateObject(
            request, new ValidationContext(request), [], validateAllProperties: true));
    }

    [Fact]
    public void Missing_price_is_rejected_but_a_free_ticket_is_valid()
    {
        var missing = new PurchaseTicketsRequest
        {
            PricingTierId = Guid.NewGuid(), CustomerEmail = "buyer@example.com", Quantity = 1
        };
        Assert.False(Validator.TryValidateObject(missing, new ValidationContext(missing), [], true));
        var free = new PurchaseTicketsRequest
        {
            PricingTierId = Guid.NewGuid(), CustomerEmail = "buyer@example.com", Quantity = 1,
            ExpectedUnitPrice = 0
        };
        Assert.True(Validator.TryValidateObject(free, new ValidationContext(free), [], true));
    }
}
