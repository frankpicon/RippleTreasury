using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Api.Contracts;
using Ticketing.Api.Services;

namespace Ticketing.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/events/availability")]
[Authorize]
public sealed class InventoryController(ITicketPurchaseService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InventorySummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InventorySummaryResponse>>> GetAll(
        CancellationToken cancellationToken) =>
        Ok(await service.GetInventorySummariesAsync(cancellationToken));
}
