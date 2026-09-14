using EventTicketing.ServiceDefaults.Errors;

namespace Ticketing.Api.Services;

public static class PurchaseRules
{
    public static void Validate(
        int quantity,
        string customerEmail,
        string idempotencyKey,
        DateTimeOffset eventStart,
        bool eventIsActive,
        DateTimeOffset nowUtc)
    {
        if (quantity is < 1 or > 20)
        {
            throw new RequestValidationException("Quantity must be between 1 and 20.");
        }

        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            throw new RequestValidationException("Customer email is required.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
        {
            throw new RequestValidationException(
                "A unique Idempotency-Key header of at most 100 characters is required.");
        }

        if (!eventIsActive)
        {
            throw new ResourceConflictException("Tickets cannot be purchased for an inactive event.");
        }

        if (eventStart <= nowUtc)
        {
            throw new ResourceConflictException("Tickets cannot be purchased after an event has started.");
        }
    }
}
