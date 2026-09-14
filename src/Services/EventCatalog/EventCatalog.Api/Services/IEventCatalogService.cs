using EventCatalog.Api.Contracts;

namespace EventCatalog.Api.Services;

public interface IEventCatalogService
{
    Task<IReadOnlyList<EventResponse>> GetAllAsync(CancellationToken cancellationToken);
    Task<EventResponse> GetAsync(Guid eventId, CancellationToken cancellationToken);
    Task<EventResponse> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken);
    Task<EventResponse> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid eventId, long version, CancellationToken cancellationToken);
}
