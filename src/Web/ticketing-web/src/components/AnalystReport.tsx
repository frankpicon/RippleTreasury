import { useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import type { EventModel, SalesSummary } from "../api/types";

interface Props {
  events: EventModel[];
  revision: number;
}

const currency = new Intl.NumberFormat(undefined, { style: "currency", currency: "USD" });

function percentage(sold: number, capacity: number) {
  return capacity === 0 ? "0.0%" : `${((sold / capacity) * 100).toFixed(1)}%`;
}

export function AnalystReport({ events, revision }: Props) {
  const [reports, setReports] = useState<Record<string, SalesSummary>>({});
  const [loading, setLoading] = useState(true);
  const [syncingCount, setSyncingCount] = useState(0);

  async function loadReports() {
    setLoading(true);
    const results = await Promise.allSettled(events.map(event => api.getSales(event.id)));
    const loaded: Record<string, SalesSummary> = {};
    let unavailable = 0;
    results.forEach((result, index) => {
      if (result.status === "fulfilled") loaded[events[index].id] = result.value;
      else unavailable += 1;
    });
    setReports(loaded);
    setSyncingCount(unavailable);
    setLoading(false);
  }

  useEffect(() => {
    void loadReports();
  }, [events, revision]);

  const ordered = useMemo(() => events
    .map(event => ({ event, report: reports[event.id] }))
    .filter((item): item is { event: EventModel; report: SalesSummary } => Boolean(item.report))
    .sort((left, right) => left.event.startsAtUtc.localeCompare(right.event.startsAtUtc)),
  [events, reports]);

  const totals = ordered.reduce((current, { report }) => ({
    capacity: current.capacity + report.totalCapacity,
    sold: current.sold + report.ticketsSold,
    available: current.available + report.available,
    revenue: current.revenue + report.grossRevenue
  }), { capacity: 0, sold: 0, available: 0, revenue: 0 });

  return (
    <section className="analyst-report">
      <div className="section-heading report-heading">
        <div><span className="eyebrow">Analyst workspace</span><h2>Portfolio sales report</h2>
          <p className="muted">Complete sales and inventory performance across every event and pricing tier.</p>
        </div>
        <button className="secondary" disabled={loading} onClick={() => void loadReports()}>
          {loading ? "Refreshing…" : "Refresh report"}
        </button>
      </div>

      {syncingCount > 0 &&
        <p className="notice">{syncingCount} event report{syncingCount === 1 ? " is" : "s are"} still syncing.</p>}

      <div className="report-metrics">
        <div><span>Events reported</span><strong>{ordered.length}</strong></div>
        <div><span>Total capacity</span><strong>{totals.capacity.toLocaleString()}</strong></div>
        <div><span>Tickets sold</span><strong>{totals.sold.toLocaleString()}</strong></div>
        <div><span>Tickets available</span><strong>{totals.available.toLocaleString()}</strong></div>
        <div><span>Sell-through</span><strong>{percentage(totals.sold, totals.capacity)}</strong></div>
        <div><span>Gross revenue</span><strong>{currency.format(totals.revenue)}</strong></div>
      </div>

      {ordered.length === 0 && !loading &&
        <div className="panel"><p className="muted">No completed event reports are available yet.</p></div>}

      {ordered.map(({ event, report }) => (
        <article className="panel report-event" key={event.id}>
          <div className="section-heading">
            <div><span className="eyebrow">{report.isActive ? "Active event" : "Inactive event"}</span>
              <h2>{event.name}</h2><p className="muted">{event.description}</p></div>
            <span className="status">{new Date(event.startsAtUtc).toLocaleString()}</span>
          </div>
          <p className="report-venue"><strong>Venue:</strong> {event.venue}</p>
          <div className="report-metrics compact">
            <div><span>Capacity</span><strong>{report.totalCapacity.toLocaleString()}</strong></div>
            <div><span>Sold</span><strong>{report.ticketsSold.toLocaleString()}</strong></div>
            <div><span>Available</span><strong>{report.available.toLocaleString()}</strong></div>
            <div><span>Sell-through</span><strong>{percentage(report.ticketsSold, report.totalCapacity)}</strong></div>
            <div><span>Revenue</span><strong>{currency.format(report.grossRevenue)}</strong></div>
          </div>
          <div className="report-table-wrap">
            <table className="report-table">
              <thead><tr><th>Pricing tier</th><th>Status</th><th>Unit price</th><th>Capacity</th>
                <th>Sold</th><th>Available</th><th>Sell-through</th><th>Revenue</th></tr></thead>
              <tbody>{report.pricingTiers.map(tier => {
                const catalogTier = event.pricingTiers.find(item => item.id === tier.pricingTierId);
                return <tr key={tier.pricingTierId}>
                  <td><strong>{tier.name}</strong></td><td>{tier.isActive ? "Active" : "Inactive"}</td>
                  <td>{catalogTier ? currency.format(catalogTier.price) : "—"}</td>
                  <td>{tier.capacity.toLocaleString()}</td><td>{tier.ticketsSold.toLocaleString()}</td>
                  <td>{tier.available.toLocaleString()}</td>
                  <td>{percentage(tier.ticketsSold, tier.capacity)}</td>
                  <td>{currency.format(tier.grossRevenue)}</td>
                </tr>;
              })}</tbody>
            </table>
          </div>
        </article>
      ))}
    </section>
  );
}