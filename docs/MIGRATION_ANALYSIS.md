# C# Migration Analysis

## Current target state

The .NET 8 implementation now includes functional WPF modules for Start, Kalender, Tours, Orders, Non-Map Orders, Employees, Vehicles, Settings, GPS and Updates with a layered App/Application/Domain/Infrastructure structure.

## Architecture mapping

- Python UI flow -> WPF shell with section-specific Views + ViewModels
- Python state snapshots -> `AppSnapshotService`
- Python backup routines -> `BackupManager`
- Python settings checks -> `SettingsValidator`
- SQL naming heuristics -> `SqlDatabaseNameInference`
- file persistence -> JSON repositories and parity-normalizers in Infrastructure

## Feature parity status (implementation phase)

Implemented in C#:
- Start snapshot dashboard (`AppSnapshotService` integration)
- Kalender month view with day-level tour preview (`KalenderSectionViewModel`, `JsonToursRepository`)
- Tours list, stop preview, ETA/ETD recalculation, assignment editing, conflict preview
- Orders and Non-Map Orders split views with search/filter and JSON save-back
- Employees CRUD with active/inactive state
- Vehicles + trailers CRUD with capacities/status/notes
- Settings load/save/validate + backup/create/restore/cleanup operations
- GPS URL flow with reload/open/copy and runtime fallback messaging
- Updates page with version/runtime/backup status

Still open / partial:
- Karte section remains a placeholder (no marker/filter/routing UI yet)
- Kalender -> direct navigation into Tours is not wired yet (data parity exists, UI jump action pending)
- WebView2 embedding is represented via fallback-ready logic; explicit embedded control integration pending
- full solution build/test execution is blocked in this environment by local SDK resolver issue (`MSB4276` for App/Infrastructure/Test restore)

## Risks and mitigation

1. UI parity risk for Karte: currently highest gap; planned dedicated iteration.
2. Data shape drift: reduced by parity repositories + normalizers and write-back normalization.
3. Settings regressions: reduced by validator tests and live validation in settings UI.
4. Scheduling/conflict behavior drift: reduced by dedicated `TourScheduleService` and `TourConflictService` tests.

## Next migration increments

1. Implement Karte with marker list, filter/search, route panel and order actions.
2. Wire Kalender day/tour interaction to auto-focus Tours section.
3. Add explicit WebView2 host control when runtime/package conditions are met.
4. Resolve local SDK/workload resolver issue and run full solution build/tests in CI and local environment.
