# API versioning validation

Validated September 22, 2026: V1 is the only published REST contract in the RippleTreasury clone and local Docker stack. All REST URLs require `/api/v1`; unversioned routes have been removed.

## Results

- Complete .NET solution build: passed, zero warnings and zero errors.
- Event Catalog unit tests: 16 passed.
- Ticketing unit tests: 21 passed.
- API versioning and gateway tests: 34 passed, including rejection of original routes and Swagger exposing only V1 endpoints.
- Container integration tests: all 17 passed on Windows against PostgreSQL and RabbitMQ Testcontainers. Total: 88 passed, zero failed, zero skipped.
- Docker production builds: Event Catalog, Ticketing, Reporting, Notifications, Realtime, gateway, and web passed. Updated containers were started from this clone.
- Deployed API smoke test passed for V1 routing, stable tier renames, quoted-price validation, idempotent replay, event-wide capacity, reporting, deletion propagation, and unversioned-route rejection. Run `pwsh -File scripts/validate-versioning.ps1` against the local demo stack to repeat the standard route validation.
- Swagger: all three live documents expose only concrete V1 routes.

The smoke test checks V1 routes, rejection of unversioned URLs, supported-version headers, V2 rejection, authentication, Swagger containing only V1 endpoints, creation links, purchases, idempotency, inventory, and asynchronous sales projection. It deletes its temporary event; the purchase remains in local audit data.

## Swagger access

- Event Catalog: http://localhost:5101/swagger/index.html
- Ticketing: http://localhost:5102/swagger/index.html
- Reporting: http://localhost:5103/swagger/index.html

Use **Authorize** with a JWT access token, then expand an endpoint and select **Try it out**. The bearer security scheme adds the `Bearer` prefix automatically. Local token retrieval is documented in `EventTicketing.http`.
