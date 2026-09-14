# Interview presentation guide

## Recommended 20-25 minute case-study narrative

### 1. Open with the outcome (2 minutes)

“I implemented every requested capability and designed the purchase boundary so it remains correct under
concurrency. The environment runs locally with one Docker Compose command and includes security, messaging,
observability, tests, and a UI.”

Show the service table in the README. Emphasize that the architecture is intentionally larger than the minimum
case-study solution because it demonstrates how the system could evolve, while the transactional core remains
simple.

### 2. Demonstrate the product (5 minutes)

1. Sign in as `admin` and create an event.
2. Explain that `201 Created` means Event Catalog committed its source-of-truth record and outbox entry.
3. Select the event and show availability after the inventory projection arrives.
4. Open a second authenticated browser, purchase tickets, and show availability and revenue update immediately.
5. Open RabbitMQ queues, Seq logs, or a Jaeger trace.
6. Mention that the notification is intentionally simulated and stored locally.

Point out that the catalog's original capacity and Ticketing's remaining inventory are intentionally different
concepts. SignalR sends a projection-completed notification to both browsers; each browser then re-reads the
authoritative API. The client does not subtract message quantities locally, which keeps reconnect and duplicate
behavior correct. Redis provides the SignalR scale-out backplane.

### 3. Explain the most important code decision (5 minutes)

Open `TicketPurchaseService.cs`. Focus on the conditional inventory update:

- It checks remaining capacity and increments sold tickets atomically.
- It works with multiple API replicas; an in-memory `lock` would not.
- The purchase and outgoing event share a transaction through the bus outbox.
- The browser creates one idempotency key per intended purchase, disables double submission, and reuses that
  key across bounded transient retries. Ticketing returns the original result on a valid replay and rejects the
  same key when the authenticated buyer or canonical request fields differ.
- The concurrency integration test proves five seats cannot produce six successful purchases.

### 4. Explain messaging reliability (4 minutes)

Use the purchase sequence in `ARCHITECTURE.md`:

- RabbitMQ provides at-least-once delivery.
- The transactional outbox prevents a database/message dual-write gap.
- Consumer inbox state removes duplicate business effects.
- Bounded retries handle transient failures; `_error` queues preserve poison messages.
- Correlation IDs connect HTTP, message, log, and trace activity.
- The HTTP middleware enriches Seq with `CorrelationId`, and every consumer logs the same value carried by
  the versioned contract. Searching one GUID therefore shows the gateway request and both service projections.

Avoid claiming literal transport-level “exactly once.” State that idempotency creates effectively-once business
outcomes.

### 5. Explain security and scale (3 minutes)

- React uses Authorization Code + PKCE with local Keycloak.
- APIs validate their own tokens and roles; the gateway is not the only security boundary.
- Services and consumers are stateless and independently scalable.
- Each service owns its database; reporting does not join across services.
- PostgreSQL atomicity, not a single-process lock, protects inventory.
- SignalR is authenticated with the same Keycloak token and Redis supports multiple Realtime API replicas.

### 6. Close with tradeoffs and learning (3 minutes)

Be candid:

- The requested exercise could be solved well as a modular monolith in two hours. Microservices add deployment,
  observability, testing, data-consistency, and operational cost.
- Reporting is intentionally eventually consistent; inventory is strongly consistent inside Ticketing.
- Local `EnsureCreated` optimizes reviewer setup; production requires migrations.
- Event changes that conflict with existing sales need a production approval saga or reservation-aware validation.
- A real notification provider, payment flow, refund/cancellation saga, and HA infrastructure are outside scope.

Conclude: “I optimized for a system whose correctness is easy to explain and whose operational risks are visible.”

## Likely questions

### Why RabbitMQ rather than Kafka?

RabbitMQ fits transactional commands/events, competing consumers, flexible routing, retry/error queues, and a
small local footprint. Kafka would be attractive if long-term replay, stream processing, or very high ordered
throughput were central requirements.

### Why not keep one database?

Shared tables create runtime and deployment coupling. Separate ownership allows independent change and scale.
One local PostgreSQL server reduces demonstration cost, but databases and credentials remain separate.

### What happens if RabbitMQ is down after purchase commit?

The purchase and outbox message commit together. The API can complete based on the authoritative database state.
The outbox dispatcher retries publishing when RabbitMQ returns; monitoring should alert on outbox age.

### What if a consumer crashes after updating its database?

Its update and inbox acknowledgement are one database transaction. Before commit, redelivery safely retries.
After commit, inbox state recognizes the message and prevents another business update.

### How do you prevent overselling?

A conditional SQL update increments `tickets_sold` only when sufficient capacity remains. Concurrent callers
compete at the database row; only valid reservations affect one row and proceed.

### Is reporting immediately consistent?

No. It is a read projection optimized for availability and independence. The purchase response and Ticketing
availability are authoritative; projection lag is monitored and exposed honestly in the UI.

### Why does SignalR send an invalidation instead of the new ticket count?

The Realtime service does not own inventory or reporting state. It broadcasts that a committed projection
changed, and the browser retrieves the authoritative value from the owning API. This remains correct if messages
are duplicated, delayed, reordered, or missed during a disconnect. A low-frequency reconciliation covers a long
offline interval.

### Why use Redis with SignalR locally?

RabbitMQ distributes a projection notification to one Realtime API consumer instance. Redis then fans the
SignalR notification across every Realtime API replica, so clients connected to different instances all update.

## Explain REST versioning

V1 is the first published REST contract. All clients call explicit `/api/v1` URLs through YARP, and
the services validate the URL version. Unversioned URLs and V2 currently return 404. For a future breaking
change, add V2 controllers and contracts while retaining V1 during client migration.

Show `/api/v1/events` returning the documented contract, and demonstrate that Swagger lists only V1
endpoints. Show unversioned URLs and `/api/v2/events` returning 404.
REST contract versions, integration message versions, and event record versions solve different problems:
client compatibility, message compatibility, and concurrent data updates.
