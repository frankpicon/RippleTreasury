using Asp.Versioning;
using EventTicketing.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reporting.Api.Data;

namespace Reporting.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reports/events")]
[Authorize(Policy = "report-reader")]
public sealed class ReportsController(ReportingDbContext db) : ControllerBase
{
    [HttpGet("{eventId:guid}/sales")]
    [ProducesResponseType<EventSalesSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventSalesSummaryResponse>> GetSalesSummary(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var projection = await db.EventSales.AsNoTracking()
            .Include(item => item.PricingTiers)
            .SingleOrDefaultAsync(item => item.EventId == eventId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Sales report for event '{eventId}' was not found.");

        var remaining = projection.IsActive
            ? Math.Max(0, projection.TotalCapacity - projection.TicketsSold) : 0;
        var tiers = projection.PricingTiers.OrderBy(item => item.Name)
            .Select(item => new TierSalesResponse(
                item.PricingTierId, item.Name, item.Capacity, item.TicketsSold,
                item.IsActive ? Math.Min(remaining, Math.Max(0, item.Capacity - item.TicketsSold)) : 0,
                item.GrossRevenue, item.IsActive))
            .ToList();

        return Ok(new EventSalesSummaryResponse(
            projection.EventId,
            projection.EventName,
            projection.StartsAtUtc,
            projection.TotalCapacity,
            projection.TicketsSold,
            Math.Min(remaining, tiers.Sum(tier => tier.Available)),
            projection.GrossRevenue,
            projection.IsActive,
            tiers));
    }
}

public sealed record TierSalesResponse(
    Guid PricingTierId,
    string Name,
    int Capacity,
    int TicketsSold,
    int Available,
    decimal GrossRevenue,
    bool IsActive);

public sealed record EventSalesSummaryResponse(
    Guid EventId,
    string EventName,
    DateTimeOffset StartsAtUtc,
    int TotalCapacity,
    int TicketsSold,
    int Available,
    decimal GrossRevenue,
    bool IsActive,
    IReadOnlyList<TierSalesResponse> PricingTiers);
