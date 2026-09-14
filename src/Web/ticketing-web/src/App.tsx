import { useEffect, useRef, useState } from "react";
import { api } from "./api/client";
import type {
  Availability,
  EventModel,
  InventorySummary,
  RealtimeUpdate
} from "./api/types";
import keycloak from "./auth/keycloak";
import { CreateEventForm } from "./components/CreateEventForm";
import { AnalystReport } from "./components/AnalystReport";
import { EventDetails } from "./components/EventDetails";
import {
  createRealtimeConnection,
  realtimeEvents,
  type RealtimeStatus
} from "./realtime/connection";

const reconciliationIntervalMilliseconds = 60_000;
const restartDelayMilliseconds = 5_000;

export default function App() {
  const [events, setEvents] = useState<EventModel[]>([]);
  const [inventory, setInventory] = useState<Record<string, InventorySummary>>({});
  const [liveAvailability, setLiveAvailability] = useState<Record<string, Availability>>({});
  const [salesRevisions, setSalesRevisions] = useState<Record<string, number>>({});
  const [salesReconciliationRevision, setSalesReconciliationRevision] = useState(0);
  const [selected, setSelected] = useState<EventModel>();
  const [error, setError] = useState("");
  const [realtimeStatus, setRealtimeStatus] = useState<RealtimeStatus>("connecting");
  const inventoryRefreshTimers = useRef<Record<string, number>>({});
  const token = keycloak.tokenParsed as (typeof keycloak.tokenParsed & {
    realm_access?: { roles?: string[] };
    preferred_username?: string;
  }) | undefined;
  const roles = keycloak.realmAccess?.roles ?? token?.realm_access?.roles ?? [];
  const username = token?.preferred_username ?? "Authenticated user";
  const canAdminister = roles.includes("event-admin");
  const canPurchase = roles.includes("ticket-buyer");
  const isAnalystOnly = roles.includes("report-reader") && !canAdminister && !canPurchase;

  function availabilityChanged(availability: Availability) {
    setLiveAvailability(current => ({ ...current, [availability.eventId]: availability }));
    setInventory(current => ({
      ...current,
      [availability.eventId]: {
        eventId: availability.eventId,
        totalCapacity: availability.totalCapacity,
        ticketsSold: availability.ticketsSold,
        available: availability.available,
        isActive: availability.isActive
      }
    }));
  }

  async function loadEvents() {
    try {
      const [eventsResult, inventoryResult] = await Promise.allSettled([
        api.getEvents(),
        api.getInventorySummaries()
      ]);
      if (eventsResult.status === "rejected") throw eventsResult.reason;
      const loaded = eventsResult.value;
      setEvents(loaded);
      if (inventoryResult.status === "fulfilled") {
        setInventory(Object.fromEntries(inventoryResult.value.map(summary =>
          [summary.eventId, summary])));
      }
      setSelected(current => loaded.find(item => item.id === current?.id) ?? loaded[0]);
      setError("");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Could not load events.");
    }
  }

  async function loadEventInventory(eventId: string) {
    try {
      availabilityChanged(await api.getAvailability(eventId));
    } catch {
      // A catalog event can arrive just before its ticketing projection is committed.
      // The projection-completed event or periodic reconciliation will retry it.
    }
  }

  async function reconcileAll() {
    await loadEvents();
    setSalesReconciliationRevision(current => current + 1);
  }

  function queueEventInventoryRefresh(eventId: string) {
    const existing = inventoryRefreshTimers.current[eventId];
    if (existing !== undefined) window.clearTimeout(existing);
    inventoryRefreshTimers.current[eventId] = window.setTimeout(() => {
      delete inventoryRefreshTimers.current[eventId];
      void loadEventInventory(eventId);
    }, 100);
  }

  useEffect(() => {
    void reconcileAll();
    const timer = window.setInterval(() => void reconcileAll(),
      reconciliationIntervalMilliseconds);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    const connection = createRealtimeConnection();
    let disposed = false;
    let restartTimer: number | undefined;

    const scheduleRestart = () => {
      if (disposed || restartTimer !== undefined) return;
      restartTimer = window.setTimeout(() => {
        restartTimer = undefined;
        void start();
      }, restartDelayMilliseconds);
    };

    const start = async () => {
      if (disposed) return;
      setRealtimeStatus("connecting");
      try {
        await connection.start();
        if (!disposed) {
          setRealtimeStatus("connected");
          await reconcileAll();
        }
      } catch {
        if (!disposed) {
          setRealtimeStatus("offline");
          scheduleRestart();
        }
      }
    };

    connection.onreconnecting(() => setRealtimeStatus("reconnecting"));
    connection.onreconnected(() => {
      setRealtimeStatus("connected");
      void reconcileAll();
    });
    connection.onclose(() => {
      if (!disposed) {
        setRealtimeStatus("offline");
        scheduleRestart();
      }
    });

    connection.on(realtimeEvents.catalogChanged, (_: RealtimeUpdate) => void loadEvents());
    connection.on(realtimeEvents.inventoryChanged, (update: RealtimeUpdate) =>
      queueEventInventoryRefresh(update.eventId));
    connection.on(realtimeEvents.salesChanged, (update: RealtimeUpdate) =>
      setSalesRevisions(current => ({
        ...current,
        [update.eventId]: (current[update.eventId] ?? 0) + 1
      })));

    void start();

    return () => {
      disposed = true;
      if (restartTimer !== undefined) window.clearTimeout(restartTimer);
      connection.off(realtimeEvents.catalogChanged);
      connection.off(realtimeEvents.inventoryChanged);
      connection.off(realtimeEvents.salesChanged);
      Object.values(inventoryRefreshTimers.current)
        .forEach(timer => window.clearTimeout(timer));
      inventoryRefreshTimers.current = {};
      void connection.stop();
    };
  }, []);

  function eventCreated(created: EventModel) {
    setEvents(current => [...current, created].sort((a, b) =>
      a.startsAtUtc.localeCompare(b.startsAtUtc)));
    setSelected(created);
  }

  const realtimeLabel = realtimeStatus === "connected"
    ? "Real-time connected"
    : realtimeStatus === "reconnecting"
      ? "Real-time reconnecting"
      : realtimeStatus === "connecting"
        ? "Connecting real-time"
        : "Real-time offline · fallback active";

  return (
    <>
      <header className="topbar">
        <div className="brand"><span className="brand-mark">EF</span><div><strong>EventFlow</strong><small>Local event-driven ticketing</small></div></div>
        <div className="identity"><span>{username}</span>
          <button className="secondary" onClick={() => void keycloak.logout({ redirectUri: window.location.origin })}>Sign out</button></div>
      </header>
      <main>
        <section className="hero">
          <div><span className="eyebrow">Microservice demonstration</span><h1>Events move. Inventory stays correct.</h1>
            <p>RabbitMQ connects independently scalable services while transactional outboxes protect every state change.</p></div>
          <div className={`system-status ${realtimeStatus}`}><span className="pulse" />{realtimeLabel}</div>
        </section>
        {error && <p className="error panel">{error}</p>}
        {isAnalystOnly ?
          <AnalystReport events={events} revision={salesReconciliationRevision +
            Object.values(salesRevisions).reduce((total, value) => total + value, 0)} /> :
        <div className="layout">
          <aside className="panel event-list">
            <div className="section-heading"><div><span className="eyebrow">Catalog</span><h2>Upcoming events</h2></div>
              <button className="icon-button" onClick={() => void loadEvents()} aria-label="Refresh events">↻</button></div>
            {events.length === 0 && <p className="muted">No events yet. Create the first one.</p>}
            {events.map(event => (
              <button key={event.id} className={`event-row ${selected?.id === event.id ? "selected" : ""}`}
                onClick={() => setSelected(event)}>
                <span className="date">{new Date(event.startsAtUtc).toLocaleDateString(undefined, { month: "short", day: "2-digit" })}</span>
                <span><strong>{event.name}</strong><small>{event.venue}</small></span>
                <span className="capacity" title={`${event.totalCapacity} total capacity`}>
                  {inventory[event.id] ? `${inventory[event.id].available} left` : "Syncing…"}
                </span>
              </button>
            ))}
          </aside>
          <div className="content">
            {selected ? <EventDetails event={selected} canPurchase={canPurchase}
              onAvailabilityChanged={availabilityChanged}
              realtimeAvailability={liveAvailability[selected.id]}
              salesRevision={(salesRevisions[selected.id] ?? 0) +
                salesReconciliationRevision} /> :
              <section className="panel empty"><h2>Select an event</h2><p>Availability and reporting appear here.</p></section>}
            {canAdminister && <CreateEventForm onCreated={eventCreated} />}
          </div>
        </div>}
      </main>
      <footer>Event Catalog · Ticketing · Reporting · Notifications · SignalR · RabbitMQ</footer>
    </>
  );
}
