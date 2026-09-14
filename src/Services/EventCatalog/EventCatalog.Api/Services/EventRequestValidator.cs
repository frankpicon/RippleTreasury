using EventCatalog.Api.Contracts;
using EventTicketing.ServiceDefaults.Errors;

namespace EventCatalog.Api.Services;

public static class EventRequestValidator
{
    public static void Validate(CreateEventRequest request, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Description) ||
            string.IsNullOrWhiteSpace(request.Venue))
        {
            throw new RequestValidationException("Name, description, and venue are required.");
        }

        if (request.TotalCapacity <= 0)
        {
            throw new RequestValidationException("Total capacity must be greater than zero.");
        }

        if (request.StartsAtUtc <= nowUtc)
        {
            throw new RequestValidationException("The event start time must be in the future.");
        }

        if (request.PricingTiers is null || request.PricingTiers.Count == 0)
        {
            throw new RequestValidationException("At least one pricing tier is required.");
        }

        if (request.PricingTiers.Any(tier => tier is null || string.IsNullOrWhiteSpace(tier.Name)))
        {
            throw new RequestValidationException("Every pricing tier must have a name.");
        }

        if (request.PricingTiers.Any(tier => tier is null || tier.Capacity <= 0 || tier.Price < 0))
        {
            throw new RequestValidationException(
                "Every pricing tier must have positive capacity and a non-negative price.");
        }

        if (request.PricingTiers.Sum(tier => tier.Capacity) != request.TotalCapacity)
        {
            throw new RequestValidationException(
                "The sum of pricing-tier capacities must equal the event's total capacity.");
        }

        var uniqueNames = request.PricingTiers
            .Select(tier => tier.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (uniqueNames != request.PricingTiers.Count)
        {
            throw new RequestValidationException("Pricing-tier names must be unique within an event.");
        }
    }
}
