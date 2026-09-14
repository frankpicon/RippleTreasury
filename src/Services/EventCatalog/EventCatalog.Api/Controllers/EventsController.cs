using Asp.Versioning;
using EventCatalog.Api.Contracts;
using EventCatalog.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventCatalog.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/events")]
[Authorize]
public sealed class EventsController(IEventCatalogService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EventResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EventResponse>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await service.GetAllAsync(cancellationToken));

    [HttpGet("{eventId:guid}")]
    [ProducesResponseType<EventResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventResponse>> Get(Guid eventId, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(eventId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = "event-admin")]
    [ProducesResponseType<EventResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EventResponse>> Create(
        CreateEventRequest request,
        CancellationToken cancellationToken)
    {
        var created = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { eventId = created.Id, version = HttpContext.GetRequestedApiVersion()!.ToString() }, created);
    }

    [HttpPut("{eventId:guid}")]
    [Authorize(Policy = "event-admin")]
    [ProducesResponseType<EventResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EventResponse>> Update(
        Guid eventId,
        UpdateEventRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.UpdateAsync(eventId, request, cancellationToken));

    [HttpDelete("{eventId:guid}")]
    [Authorize(Policy = "event-admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid eventId,
        [FromQuery] long version,
        CancellationToken cancellationToken)
    {
        await service.DeleteAsync(eventId, version, cancellationToken);
        return NoContent();
    }
}
