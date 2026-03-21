# C# Migration Analysis

## Current target state

The .NET 8 implementation now includes functional WPF modules for Start, Kalender, Tours, Orders, Non-Map Orders, Employees, Vehicles, Settings, GPS and Updates with a layered App/Application/Domain/Infrastructure structure.

Phase 5 summary:
- completion details and residual tasks are captured in `docs/PHASE5_COMPLETION_REPORT.md`.

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
- Karte with embedded WebView2 map tile host, marker synchronization, search/filter, route panel and save-to-tour actions
- GPS URL flow with reload/open/copy and embedded WebView2 host plus runtime fallback messaging
- Updates page with version/runtime/backup status

Still open / partial:
- full solution build/test execution is blocked in this environment by local SDK resolver issue (`MSB4276` for App/Infrastructure/Test restore)

## Risks and mitigation

1. Map parity risk: tile host is now embedded; advanced map interactions (drag route edits, richer overlays) still remain for full parity.
2. Data shape drift: reduced by parity repositories + normalizers and write-back normalization.
3. Settings regressions: reduced by validator tests and live validation in settings UI.
4. Scheduling/conflict behavior drift: reduced by dedicated `TourScheduleService` and `TourConflictService` tests.

## Next migration increments

1. Wire Kalender day/tour interaction to auto-focus Tours section.
2. Add explicit map tile host + marker rendering layer for full visual parity.
3. Resolve local SDK/workload resolver issue and run full solution build/tests in CI and local environment.
