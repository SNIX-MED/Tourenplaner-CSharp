# PostgreSQL Multiuser Migration

## Why this migration is necessary

The current application is optimized for local JSON files and single-user editing sessions.
Large parts of the UI load entire collections into memory and persist them back as complete snapshots.
That approach is not safe for concurrent multiuser editing.

## Current blockers in the existing architecture

- The WPF layer still talks directly to JSON-based parity repositories in many places.
- Many workflows use `LoadAsync()` / `GetAllAsync()` followed by in-memory mutation and a full `SaveAsync()` / `SaveAllAsync()`.
- In-process refresh notifications do not synchronize across machines.
- Shared JSON files would still suffer from overwrite races even if moved to a central share.

## Migration strategy

### Phase 1: Foundation

- Add PostgreSQL configuration to `AppSettings`
- Add PostgreSQL connection and schema bootstrap infrastructure
- Add repository implementations for the application abstractions

This phase creates the technical base but does not yet switch the WPF application to PostgreSQL.

### Phase 2: Repository seam cleanup

- Replace direct usage of parity JSON repositories in the WPF layer with application abstractions
- Introduce a repository factory or storage-mode composition root in startup
- Keep JSON mode as fallback during rollout

### Phase 3: Multiuser-safe write model

- Replace bulk `SaveAll` usage in critical flows with targeted operations:
  - upsert order
  - archive/unarchive order
  - assign/unassign order to tour
  - create/update/delete tour
  - update employees and vehicles independently
- Add optimistic concurrency, for example `updated_at` or `xmin`/version checks
- Detect and surface conflicts in the UI instead of silently overwriting

### Phase 4: Shared runtime behavior

- Add cross-client refresh strategy
- Reload changed entities after successful writes
- Add optional polling or database-notify based invalidation

## Database shape for the first PostgreSQL step

The initial infrastructure added in this change creates these tables:

- `app.orders`
- `app.tours`
- `app.employees`
- `app.vehicles`
- `app.singletons`

The current repository scaffolding stores payloads as `jsonb`.
That keeps the first migration step small, while later phases can move critical entities to more normalized table shapes where needed.

## Important note

The first PostgreSQL scaffolding is intentionally not the final multiuser design.
It is the safe starting point for the real migration, because true multiuser support requires application-level write refactoring, not just a storage backend swap.
