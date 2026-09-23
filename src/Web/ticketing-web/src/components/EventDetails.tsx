import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError, isTransientRequestFailure } from "../api/client";
import type {
  Availability,
  EventModel,
  PurchaseRequest,
  SalesSummary
} from "../api/types";

interface Props {
  event: EventModel;
  canPurchase: boolean;
  onAvailabilityChanged: (availability: Availability) => void;
  realtimeAvailability?: Availability;
  salesRevision: number;
}

interface PurchaseAttempt {
  fingerprint: string;
  idempotencyKey: string;
  request: PurchaseRequest;
}

export function EventDetails({
  event,
  canPurchase,
  onAvailabilityChanged,
  realtimeAvailability,
  salesRevision
}: Props) {
  const [availability, setAvailability] = useState<Availability>();
  const [sales, setSales] = useState<SalesSummary>();
  const [feedback, setFeedback] = useState("");
  const [inventoryPending, setInventoryPending] = useState(false);
  const [salesPending, setSalesPending] = useState(false);
  const [loading, setLoading] = useState(true);
  const [purchasing, setPurchasing] = useState(false);
  const [purchaseConfirmed, setPurchaseConfirmed] = useState(false);
  const [hasPendingRetry, setHasPendingRetry] = useState(false);
  const purchaseLocked = useRef(false);
  const pendingAttempt = useRef<PurchaseAttempt | undefined>(undefined);

  async function refreshAvailability() {
    try {
      const current = await api.getAvailability(event.id);
      setAvailability(current);
      onAvailabilityChanged(current);
      setInventoryPending(false);
    } catch (caught) {
      setInventoryPending(caught instanceof ApiError && caught.status === 404);
      throw caught;
    }
  }

  async function refreshSales() {
    try {
      setSales(await api.getSales(event.id));
      setSalesPending(false);
    } catch (caught) {
      setSalesPending(caught instanceof ApiError && caught.status === 404);
      throw caught;
    }
  }

  async function refresh(showLoading = true) {
    if (showLoading) setLoading(true);
    await Promise.allSettled([refreshAvailability(), refreshSales()]);
    if (showLoading) setLoading(false);
  }

  useEffect(() => {
    setAvailability(undefined);
    setSales(undefined);
    setFeedback("");
    setInventoryPending(false);
    setSalesPending(false);
    setLoading(true);
    purchaseLocked.current = false;
    setPurchaseConfirmed(false);
    pendingAttempt.current = undefined;
    setHasPendingRetry(false);
    void refresh();
  }, [event.id]);

  useEffect(() => {
    if (realtimeAvailability?.eventId === event.id) {
      setAvailability(realtimeAvailability);
      setInventoryPending(false);
    }
  }, [event.id, realtimeAvailability]);

  useEffect(() => {
    if (salesRevision === 0) return;
    const timer = window.setTimeout(() => void refreshSales().catch(() => undefined), 100);
    return () => window.clearTimeout(timer);
  }, [event.id, salesRevision]);

  async function purchase(formEvent: FormEvent<HTMLFormElement>) {
    formEvent.preventDefault();
    if (purchaseLocked.current) return;

    const form = new FormData(formEvent.currentTarget);
    const selectedTier = availability?.pricingTiers.find(
      tier => tier.pricingTierId === String(form.get("tier")));
    if (!selectedTier) return;
    const request: PurchaseRequest = {
      pricingTierId: String(form.get("tier") ?? ""),
      expectedUnitPrice: selectedTier.price,
      customerEmail: String(form.get("email") ?? "").trim(),
      quantity: Number(form.get("quantity"))
    };
    const fingerprint = JSON.stringify({ eventId: event.id,
      pricingTierId: request.pricingTierId, customerEmail: request.customerEmail,
      quantity: request.quantity });
    const attempt = pendingAttempt.current?.fingerprint === fingerprint
      ? pendingAttempt.current
      : { fingerprint, idempotencyKey: crypto.randomUUID(), request };

    pendingAttempt.current = attempt;
    purchaseLocked.current = true;
    setPurchasing(true);
    setHasPendingRetry(false);
    setFeedback("");

    try {
      await api.purchase(event.id, attempt.request, attempt.idempotencyKey);
      pendingAttempt.current = undefined;
      setPurchaseConfirmed(true);
      setFeedback("Purchase confirmed. Live inventory and reporting updates were published.");
      await refreshAvailability().catch(() => undefined);
    } catch (caught) {
      purchaseLocked.current = false;
      if (isTransientRequestFailure(caught)) {
        setHasPendingRetry(true);
        setFeedback(
          "The purchase outcome could not be confirmed. Retry to safely reuse the same request key.");
      } else {
        pendingAttempt.current = undefined;
        setFeedback(caught instanceof Error ? caught.message : "Purchase failed.");
        if (caught instanceof ApiError && caught.status === 409)
          await refreshAvailability().catch(() => undefined);
      }
    } finally {
      setPurchasing(false);
    }
  }

  function startAnotherPurchase() {
    purchaseLocked.current = false;
    pendingAttempt.current = undefined;
    setPurchaseConfirmed(false);
    setHasPendingRetry(false);
    setFeedback("");
  }

  return (
    <section className="panel details">
      <div className="section-heading">
        <div><span className="eyebrow">Selected event</span><h2>{event.name}</h2></div>
        <button className="secondary" onClick={() => void refresh()}>{loading ? "Loading…" : "Refresh"}</button>
      </div>
      <p className="muted">{event.description}</p>
      <div className="facts">
        <div><span>Venue</span><strong>{event.venue}</strong></div>
        <div><span>Starts</span><strong>{new Date(event.startsAtUtc).toLocaleString()}</strong></div>
        <div><span>Available</span><strong>{availability?.available ?? "—"}</strong></div>
        <div><span>Revenue</span><strong>{sales ? `$${sales.grossRevenue.toFixed(2)}` : "—"}</strong></div>
      </div>
      {(inventoryPending || salesPending) &&
        <p className="notice">The event is created. Messaging projections are still catching up.</p>}
      {feedback && <p className="notice">{feedback}</p>}
      {canPurchase && availability && availability.pricingTiers.some(tier => tier.isActive) && (
        <form className="purchase" onSubmit={purchase}>
          <label>Tier<select name="tier" disabled={purchasing || purchaseConfirmed}>
            {availability.pricingTiers.filter(tier => tier.isActive).map(tier =>
              <option key={tier.pricingTierId} value={tier.pricingTierId}>
                {tier.name} — ${tier.price.toFixed(2)} ({tier.available} left)
              </option>)}
          </select></label>
          <label>Email<input name="email" type="email" defaultValue="buyer@example.com"
            disabled={purchasing || purchaseConfirmed} required /></label>
          <label>Quantity<input name="quantity" type="number" min="1" max="20" defaultValue="1"
            disabled={purchasing || purchaseConfirmed} required /></label>
          <div className="purchase-actions">
            <button className="primary" disabled={purchasing || purchaseConfirmed}>
              {purchasing ? "Processing…" : purchaseConfirmed ? "Purchase confirmed" :
                hasPendingRetry ? "Retry purchase" : "Purchase"}
            </button>
            {purchaseConfirmed &&
              <button type="button" className="secondary" onClick={startAnotherPurchase}>
                Make another purchase
              </button>}
          </div>
        </form>
      )}
    </section>
  );
}
