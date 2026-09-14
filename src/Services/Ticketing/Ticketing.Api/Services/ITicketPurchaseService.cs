using Ticketing.Api.Contracts;

namespace Ticketing.Api.Services;

public interface ITicketPurchaseService
{
    Task<PurchaseExecutionResult> PurchaseAsync(
        Guid eventId,
        PurchaseTicketsRequest request,
        string idempotencyKey,
        string buyerSubject,
        CancellationToken cancellationToken);

    Task<PurchaseResponse> GetPurchaseAsync(
        Guid eventId,
        Guid purchaseId,
        string buyerSubject,
        bool isAdministrator,
        CancellationToken cancellationToken);

    Task<AvailabilityResponse> GetAvailabilityAsync(Guid eventId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventorySummaryResponse>> GetInventorySummariesAsync(
        CancellationToken cancellationToken);
}

public sealed record PurchaseExecutionResult(PurchaseResponse Purchase, bool WasReplayed);
