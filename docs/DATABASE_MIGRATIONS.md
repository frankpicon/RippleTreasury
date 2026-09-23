# Database migrations

Event Catalog, Ticketing, Reporting, and Notifications each own an EF Core migration history and a checked-in
initial migration, including their inbox/outbox tables. No service calls `EnsureCreated`.

## New installations and deployments

Development and Testing apply migrations before the HTTP server or message bus starts. Other environments
check for pending migrations and fail startup if any remain. Run one migration job per database before starting
or rolling out application replicas; do not run multiple migration jobs against the same database concurrently.

Each database-owning application supports `--migrate`. It applies migrations and exits without starting its
HTTP server or message consumers. Supply the usual configuration, including `ConnectionStrings__Database`
and the authentication authority. For example, from the deployed application directory:

```powershell
dotnet Ticketing.Api.dll --migrate
```

Use a deployment identity with DDL permission for this command; normal application identities need only
runtime permissions. Back up the database and review migration SQL before a production deployment.

For local development, restore the pinned EF tool and generate a reviewed SQL script:

```powershell
dotnet tool restore
dotnet ef migrations script --idempotent --project src/Services/Ticketing/Ticketing.Api --output ticketing-migrations.sql
```

Repeat for `src/Services/EventCatalog/EventCatalog.Api`, `src/Services/Reporting/Reporting.Api`, and
`src/Services/Notifications/Notifications.Worker`. Add future changes with `dotnet ef migrations add <Name>
--project <project>` and commit the migration, designer, and model snapshot together. Use
`dotnet ef migrations has-pending-model-changes --project <project>` to check that the snapshot is current.

## Existing databases created by EnsureCreated

The initial migrations describe the previous schema; these fixes do not change persistent entity columns.
An existing database has application tables but no migration history. Do **not** run the initial creation SQL
against those tables, and do not delete a database to upgrade it.

1. Back up the database and stop writes during the baseline operation.
2. Apply the initial migration to an empty scratch database using the same PostgreSQL version.
3. Compare the existing and scratch schemas, including columns, types, nullability, keys, indexes, foreign
   keys, and inbox/outbox tables. `pg_dump --schema-only --no-owner --no-privileges` can help; exclude
   `public.__EFMigrationsHistory` and dump-generated session markers from the comparison. Resolve any real
   differences before continuing. Do not mark an incompatible schema as migrated.
4. After confirming equivalence, create the migration history table and record **only** that service's initial
   migration. Obtain its exact ID from the `[Migration("...")]` attribute in that service's initial migration
   designer. Run the following against that service's database, replacing the placeholder with the verified ID:

```sql
BEGIN;
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('VERIFIED_SERVICE_INITIAL_MIGRATION_ID', '8.0.8')
ON CONFLICT ("MigrationId") DO NOTHING;
COMMIT;
```

5. Run the normal `--migrate` deployment step, verify there are no pending migrations, and restart the service.

Startup intentionally does not guess that an existing schema is compatible or silently baseline it.
