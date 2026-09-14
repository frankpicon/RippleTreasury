using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Api.Contracts;
using Ticketing.Api.Services;

namespace Ticketing.Api.Controllers;

[ApiController]
[Route("api/events/{eventId:guid}")]
[Authorize]
public sealed class TicketsController(ITicketPurchaseService service) : ControllerBase
{
    [HttpPost("tickets")]
    [Authorize(Policy = "ticket-buyer")]
    [ProducesResponseType<PurchaseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PurchaseResponse>> Purchase(
        Guid eventId,
        PurchaseTicketsRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "unknown";
        var result = await service.PurchaseAsync(eventId, request, idempotencyKey ?? string.Empty,
            subject, cancellationToken);
        Response.Headers["Idempotency-Replayed"] = result.WasReplayed ? "true" : "false";
        return CreatedAtAction(nameof(GetPurchase),
            new { eventId, purchaseId = result.Purchase.PurchaseId }, result.Purchase);
    }

    [HttpGet("tickets/{purchaseId:guid}")]
    [ProducesResponseType<PurchaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseResponse>> GetPurchase(
        Guid eventId,
        Guid purchaseId,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "unknown";
        var isAdministrator = User.IsInRole("event-admin");
        return Ok(await service.GetPurchaseAsync(eventId, purchaseId, subject, isAdministrator,
            cancellationToken));
    }

    [HttpGet("availability")]
    [ProducesResponseType<AvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailability(
        Guid eventId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetAvailabilityAsync(eventId, cancellationToken));
}
