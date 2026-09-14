# API versioning validation

Validated September 14, 2026: V1 is the only published REST contract in the RippleTreasury clone and local Docker stack. All REST URLs require `/api/v1`; unversioned routes have been removed.

## Results

- Complete .NET solution build: passed, zero warnings and zero errors.
- Event Catalog unit tests: 14 passed.
- Ticketing unit tests: 16 passed.
- API versioning and gateway tests: 34 passed, including rejection of original routes and Swagger exposing only V1 endpoints.
- Container integration tests: all 6 passed in the same Linux test run as the unit and versioning suites. Total: 70 passed, zero failed, zero skipped.
- Docker production builds: Event Catalog, Ticketing, Reporting, gateway, and web passed. Updated containers were started from this clone.
- Deployed API smoke test: all 29 checks passed. Run `pwsh -File scripts/validate-versioning.ps1` against the local demo stack to repeat it.
- Browser: administrator login, catalog, inventory, sales figures, and SignalR connection verified. No browser console errors were reported.
- Swagger: all three live documents expose only concrete V1 routes. The refreshed Ticketing Swagger UI was visually verified with the original entries removed.

The smoke test checks V1 routes, rejection of unversioned URLs, supported-version headers, V2 rejection, authentication, Swagger containing only V1 endpoints, creation links, purchases, idempotency, inventory, and asynchronous sales projection. It deletes its temporary event; the purchase remains in local audit data.

## Integration runner limitation

Windows runs timed out during Testcontainers' Docker named-pipe operations before application assertions. Selecting Docker Desktop's explicit Linux-engine pipe did not resolve those timeouts. The same compiled tests ran successfully inside `mcr.microsoft.com/dotnet/sdk:8.0`, using the Docker socket and `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`.

The Windows-generated `MvcTestingAppManifest.json` required a Linux path mapping. A generated copy in `bin/verification/LinuxMvcTestingAppManifest.json` was mounted over that manifest only inside the runner. Test cases and application source were not altered for this workaround. The current V1-only run passed all four test assemblies together.

Current TRX evidence: `tests/Ticketing.IntegrationTests/TestResults/V1Only/_587017bb7471_2026-09-14_04_37_03.trx`. This is a generated artifact and is not committed.

## Swagger access

- Event Catalog: http://localhost:5101/swagger/index.html
- Ticketing: http://localhost:5102/swagger/index.html
- Reporting: http://localhost:5103/swagger/index.html

Use **Authorize** with a JWT access token, then expand an endpoint and select **Try it out**. The bearer security scheme adds the `Bearer` prefix automatically. Local token retrieval is documented in `EventTicketing.http`.
