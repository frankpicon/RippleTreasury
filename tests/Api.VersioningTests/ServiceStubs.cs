using EventCatalog.Api.Contracts;
using EventCatalog.Api.Services;
using Ticketing.Api.Contracts;
using Ticketing.Api.Services;

namespace Api.VersioningTests;

internal sealed class CatalogStub : IEventCatalogService
{
    private static readonly EventResponse Event = new(VersioningHost.EventId, "Version test", "Description",
        "Hall", DateTimeOffset.Parse("2030-01-01T00:00:00Z"), 10, 1,
        [new(VersioningHost.TierId, "General", 10, 10)]);
    public Task<IReadOnlyList<EventResponse>> GetAllAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<EventResponse>>([Event]);
    public Task<EventResponse> GetAsync(Guid id, CancellationToken token) => Task.FromResult(Event);
    public Task<EventResponse> CreateAsync(CreateEventRequest request, CancellationToken token) => Task.FromResult(Event);
    public Task<EventResponse> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken token) =>
        Task.FromResult(Event with { Version = 2 });
    public Task DeleteAsync(Guid id, long version, CancellationToken token) => Task.CompletedTask;
}

internal sealed class TicketingStub : ITicketPurchaseService
{
    private static readonly PurchaseResponse Purchase = new(VersioningHost.PurchaseId, VersioningHost.EventId,
        VersioningHost.TierId, "General", "buyer@example.com", 2, 10, 20,
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    public Task<PurchaseExecutionResult> PurchaseAsync(Guid eventId, PurchaseTicketsRequest request,
        string key, string subject, CancellationToken token) => Task.FromResult(new PurchaseExecutionResult(Purchase, false));
    public Task<PurchaseResponse> GetPurchaseAsync(Guid eventId, Guid purchaseId, string subject,
        bool admin, CancellationToken token) => Task.FromResult(Purchase);
    public Task<AvailabilityResponse> GetAvailabilityAsync(Guid eventId, CancellationToken token) =>
        Task.FromResult(new AvailabilityResponse(eventId, "Version test",
            DateTimeOffset.Parse("2030-01-01T00:00:00Z"), 10, 2, 8, true, []));
    public Task<IReadOnlyList<InventorySummaryResponse>> GetInventorySummariesAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<InventorySummaryResponse>>([new(VersioningHost.EventId, 10, 2, 8, true)]);
}
