import { useState, type FormEvent } from "react";
import { api } from "../api/client";
import type { EventModel } from "../api/types";

interface Props {
  onCreated: (event: EventModel) => void;
}

export function CreateEventForm({ onCreated }: Props) {
  const defaultDate = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000)
    .toISOString().slice(0, 16);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");

  async function submit(formEvent: FormEvent<HTMLFormElement>) {
    formEvent.preventDefault();
    setSubmitting(true);
    setError("");
    const formElement = formEvent.currentTarget;
    const form = new FormData(formElement);
    const capacity = Number(form.get("capacity"));

    try {
      const created = await api.createEvent({
        name: form.get("name"),
        description: form.get("description"),
        venue: form.get("venue"),
        startsAtUtc: new Date(String(form.get("startsAt"))).toISOString(),
        totalCapacity: capacity,
        pricingTiers: [{
          name: "General Admission",
          price: Number(form.get("price")),
          capacity
        }]
      });
      onCreated(created);
      formElement.reset();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Could not create the event.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form className="panel form" onSubmit={submit}>
      <div className="section-heading">
        <div>
          <span className="eyebrow">Administrator</span>
          <h2>Create an event</h2>
        </div>
        <span className="status live">Publishes EventCreatedV1</span>
      </div>
      <div className="form-grid">
        <label>Name<input name="name" required maxLength={200} placeholder="Architecture Summit" /></label>
        <label>Venue<input name="venue" required maxLength={300} placeholder="Grand Hall" /></label>
        <label className="wide">Description<textarea name="description" required maxLength={2000}
          placeholder="A concise description of the event." /></label>
        <label>Start time<input name="startsAt" type="datetime-local" defaultValue={defaultDate} required /></label>
        <label>Capacity<input name="capacity" type="number" min="1" max="1000000" defaultValue="100" required /></label>
        <label>Ticket price<input name="price" type="number" min="0" step="0.01" defaultValue="75" required /></label>
      </div>
      {error && <p className="error">{error}</p>}
      <button className="primary" disabled={submitting}>{submitting ? "Creating…" : "Create event"}</button>
    </form>
  );
}
