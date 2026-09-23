# Codex workstation handoff

Use this file to resume EventFlow work from a new Codex task or workstation. The repository and its Git history
are the durable source of truth; local Codex task history, terminal state, Docker containers, and filesystem
permissions do not transfer between computers.

## Resume instructions

1. Clone or pull this repository and open its root folder in Codex.
2. Ask Codex to read `README.md`, `docs/ARCHITECTURE.md`, and this file before changing code.
3. Ask Codex to inspect `git log --oneline -10` and `git status --short` to verify the checkout.
4. Start Docker Desktop, then run `dotnet test EventTicketing.sln --no-restore --verbosity minimal --maxcpucount:1`.
5. Run `npm ci` and `npm run build` from `src/Web/ticketing-web` when frontend dependencies are not restored.

Suggested first prompt:

```text
Continue work on the EventFlow RippleTreasury project. First read README.md,
docs/ARCHITECTURE.md, and docs/CODEX_HANDOFF.md. Inspect the recent Git history and
working tree, then verify the full test suite before making changes. Treat the repository
as the source of truth and update CODEX_HANDOFF.md when an architectural decision changes.
```

## Application architecture

EventFlow is a .NET 8 and React event-driven ticketing platform. YARP routes versioned `/api/v1` requests.
Event Catalog owns event definitions and pricing; Ticketing owns inventory and purchases; Reporting owns sales
projections; Notifications records simulated confirmations; Realtime broadcasts SignalR invalidations through
a Redis backplane. PostgreSQL is used per service, RabbitMQ and MassTransit provide asynchronous integration,
Keycloak provides OIDC/RBAC, and Serilog, Seq, OpenTelemetry, and Jaeger provide observability.

The purchase consistency boundary is Ticketing's PostgreSQL transaction. It uses an event-scoped transaction
advisory lock, event-wide capacity validation, an atomic conditional tier update, a unique idempotency key, and
a transactional outbox. Reporting and notifications are eventually consistent and use MassTransit inbox/outbox
support for safe redelivery.

## Completed architectural fixes

- Pricing tiers retain stable IDs when renamed; IDs are omitted only for new tiers.
- Purchases require `expectedUnitPrice`; stale prices return `409` without reserving inventory.
- Browser retries retain the original request and idempotency key while using a new correlation ID per attempt.
- Ticketing serializes purchase and catalog projection writes with a PostgreSQL event-scoped transaction lock.
- Event capacity includes sales from retired tiers, preventing tier replacement from creating extra seats.
- Ticketing and Reporting persist versioned deletion tombstones so old messages cannot resurrect an event.
- Reporting serializes catalog and purchase projection mutations by event, preventing concurrent consumers from
  overwriting newer versions or calculating capacity from stale sales.
- Event and existing-tier capacities are monotonic after creation: they may increase but cannot decrease, and
  existing tiers cannot be removed. This avoids Catalog/Ticketing capacity disagreement without synchronous calls.
- Authentication bypass is rejected outside Development and Testing.
- All database-owning services use checked-in EF Core migrations and support an explicit `--migrate` deployment.
- All public REST endpoints require explicit `/api/v1` routes.

## Recent commits

- `549457a` — Reporting event locks and monotonic event/tier capacity policy.
- `c07d713` — Corrected the Mermaid purchase-consistency diagram.
- `d6c4423` — Stable pricing tiers, deletion ordering, quoted pricing, migrations, and related regression coverage.

Use `git log` as the authority if these short hashes change through rebasing.

## Last verification

After Docker Desktop became reachable, the complete solution passed:

- Event Catalog unit tests: 19
- Ticketing unit tests: 21
- Ticketing integration tests: 17
- API versioning tests: 34
- Total: 91 passed, 0 failed

The integration tests use Testcontainers for PostgreSQL and RabbitMQ. A Docker API failure can prevent fixture
startup without indicating an application test failure.

## Current architectural follow-ups

The high-priority correctness findings have been resolved. Remaining improvements are operational or scaling
enhancements: dependency-aware readiness checks, gateway admission control and rate limits, SignalR event groups
and invalidation coalescing, pagination for collection endpoints, database check constraints, and less expensive
browser reconciliation. Reassess and test each item before implementation rather than treating this list as an
automatic requirement.

## Keeping this handoff useful

Update this file when a change alters a service boundary, consistency rule, integration contract, deployment
procedure, or important test result. Normal implementation details belong in code and commit messages.
