using EventTicketing.Contracts.Messages;
using EventTicketing.ServiceDefaults.Correlation;
using EventTicketing.ServiceDefaults.Errors;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ticketing.Api.Contracts;
using Ticketing.Api.Data;
using Ticketing.Api.Domain;

namespace Ticketing.Api.Services;

public sealed class TicketPurchaseService(
    TicketingDbContext db,
    IPublishEndpoint publisher,
    ICorrelationContext correlationContext,
    TimeProvider timeProvider) : ITicketPurchaseService
{
    public async Task<PurchaseExecutionResult> PurchaseAsync(
        Guid eventId,
        PurchaseTicketsRequest request,
        string idempotencyKey,
        string buyerSubject,
        CancellationToken cancellationToken)
    {
        var existing = await db.TicketPurchases.AsNoTracking()
            .SingleOrDefaultAsync(purchase => purchase.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return MatchExisting(existing, eventId, request, buyerSubject);
        }

        var eventInfo = await db.Events.AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == eventId, cancellationToken)
            ?? throw new ResourceNotFoundException(
                $"Event '{eventId}' is not available in ticket inventory yet.");

        PurchaseRules.Validate(request.Quantity, request.CustomerEmail, idempotencyKey,
            eventInfo.StartsAtUtc, eventInfo.IsActive, timeProvider.GetUtcNow());

        var tier = await db.PricingTiers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == request.PricingTierId &&
                                               candidate.EventId == eventId,
                cancellationToken)
            ?? throw new ResourceNotFoundException(
                $"Pricing tier '{request.PricingTierId}' was not found for event '{eventId}'.");

        if (!tier.IsActive)
        {
            throw new ResourceConflictException("The selected pricing tier is inactive.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var rowsUpdated = await db.PricingTiers
                .Where(candidate => candidate.Id == tier.Id &&
                                    candidate.IsActive &&
                                    candidate.Capacity - candidate.TicketsSold >= request.Quantity)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(candidate => candidate.TicketsSold,
                        candidate => candidate.TicketsSold + request.Quantity), cancellationToken);

            if (rowsUpdated != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                var concurrentDuplicate = await db.TicketPurchases.AsNoTracking()
                    .SingleOrDefaultAsync(purchase => purchase.IdempotencyKey == idempotencyKey,
                        cancellationToken);
                if (concurrentDuplicate is not null)
                {
                    return MatchExisting(concurrentDuplicate, eventId, request, buyerSubject);
                }

                throw new ResourceConflictException(
                    "There are not enough tickets remaining in the selected pricing tier.");
            }

            var purchasedAt = timeProvider.GetUtcNow();
            var purchase = new TicketPurchase
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                PricingTierId = tier.Id,
                PricingTierName = tier.Name,
                CustomerEmail = request.CustomerEmail.Trim().ToLowerInvariant(),
                BuyerSubject = buyerSubject,
                IdempotencyKey = idempotencyKey,
                Quantity = request.Quantity,
                UnitPrice = tier.Price,
                TotalPrice = tier.Price * request.Quantity,
                CorrelationId = correlationContext.CorrelationId,
                PurchasedAtUtc = purchasedAt
            };

            db.TicketPurchases.Add(purchase);
            var message = new TicketsPurchasedV1(
                purchase.Id, purchase.EventId, purchase.PricingTierId, purchase.PricingTierName,
                purchase.CustomerEmail, purchase.Quantity, purchase.UnitPrice, purchase.TotalPrice,
                purchase.CorrelationId, purchase.PurchasedAtUtc);
            await publisher.Publish(message, context => context.CorrelationId = message.CorrelationId,
                cancellationToken);
            var inventoryChanged = new InventoryProjectionChangedV1(
                purchase.EventId, "tickets-purchased", purchase.CorrelationId, purchasedAt);
            await publisher.Publish(inventoryChanged,
                context => context.CorrelationId = inventoryChanged.CorrelationId,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PurchaseExecutionResult(Map(purchase), WasReplayed: false);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                  { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var concurrentPurchase = await db.TicketPurchases.AsNoTracking()
                .SingleAsync(purchase => purchase.IdempotencyKey == idempotencyKey, cancellationToken);
            return MatchExisting(concurrentPurchase, eventId, request, buyerSubject);
        }
    }

    public async Task<AvailabilityResponse> GetAvailabilityAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var entity = await db.Events.AsNoTracking()
            .Include(item => item.PricingTiers)
            .SingleOrDefaultAsync(item => item.Id == eventId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Event '{eventId}' was not found in ticket inventory.");

        var tiers = entity.PricingTiers.OrderBy(tier => tier.Price)
            .Select(tier => new TierAvailabilityResponse(
                tier.Id, tier.Name, tier.Price, tier.Capacity, tier.TicketsSold,
                tier.IsActive ? Math.Max(0, tier.Capacity - tier.TicketsSold) : 0,
                tier.IsActive))
            .ToList();
        var totalCapacity = tiers.Sum(tier => tier.IsActive ? tier.Capacity : tier.TicketsSold);
        var sold = tiers.Sum(tier => tier.TicketsSold);

        return new AvailabilityResponse(entity.Id, entity.Name, entity.StartsAtUtc,
            totalCapacity, sold, Math.Max(0, totalCapacity - sold), entity.IsActive, tiers);
    }

    public async Task<IReadOnlyList<InventorySummaryResponse>> GetInventorySummariesAsync(
        CancellationToken cancellationToken)
    {
        var summaries = await db.Events.AsNoTracking()
            .OrderBy(entity => entity.StartsAtUtc)
            .Select(entity => new
            {
                EventId = entity.Id,
                TotalCapacity = entity.PricingTiers.Sum(tier =>
                    tier.IsActive ? tier.Capacity : tier.TicketsSold),
                TicketsSold = entity.PricingTiers.Sum(tier => tier.TicketsSold),
                entity.IsActive
            })
            .ToListAsync(cancellationToken);

        return summaries.Select(summary => new InventorySummaryResponse(
                summary.EventId,
                summary.TotalCapacity,
                summary.TicketsSold,
                Math.Max(0, summary.TotalCapacity - summary.TicketsSold),
                summary.IsActive))
            .ToList();
    }

    public async Task<PurchaseResponse> GetPurchaseAsync(
        Guid eventId,
        Guid purchaseId,
        string buyerSubject,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        var purchase = await db.TicketPurchases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == purchaseId && item.EventId == eventId,
                cancellationToken);

        if (purchase is null ||
            (!isAdministrator && !string.Equals(purchase.BuyerSubject, buyerSubject,
                StringComparison.Ordinal)))
        {
            throw new ResourceNotFoundException($"Purchase '{purchaseId}' was not found.");
        }

        return Map(purchase);
    }

    private static PurchaseExecutionResult MatchExisting(
        TicketPurchase existing,
        Guid eventId,
        PurchaseTicketsRequest request,
        string buyerSubject)
    {
        var normalizedEmail = request.CustomerEmail.Trim().ToLowerInvariant();
        if (existing.EventId != eventId ||
            existing.PricingTierId != request.PricingTierId ||
            existing.Quantity != request.Quantity ||
            !string.Equals(existing.BuyerSubject, buyerSubject, StringComparison.Ordinal) ||
            !string.Equals(existing.CustomerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new ResourceConflictException(
                "The Idempotency-Key was already used for a different purchase request.");
        }

        return new PurchaseExecutionResult(Map(existing), WasReplayed: true);
    }

    private static PurchaseResponse Map(TicketPurchase purchase) => new(
        purchase.Id, purchase.EventId, purchase.PricingTierId, purchase.PricingTierName,
        purchase.CustomerEmail, purchase.Quantity, purchase.UnitPrice, purchase.TotalPrice,
        purchase.PurchasedAtUtc);
}
