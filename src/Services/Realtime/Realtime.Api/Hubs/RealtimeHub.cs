using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Realtime.Api.Hubs;

public sealed record RealtimeUpdate(
    Guid EventId,
    string ChangeType,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);

public interface IRealtimeClient
{
    Task CatalogChanged(RealtimeUpdate update);
    Task InventoryChanged(RealtimeUpdate update);
    Task SalesChanged(RealtimeUpdate update);
}

[Authorize]
public sealed class RealtimeHub : Hub<IRealtimeClient>
{
}
