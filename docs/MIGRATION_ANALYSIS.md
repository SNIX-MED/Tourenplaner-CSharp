# C# Migration Analysis

## Current target state

The .NET 8 implementation mirrors the Python navigation and separates concerns into App, Application, Domain, Infrastructure, and Tests.

## Architecture mapping

- Python UI flow -> WPF shell with section-specific Views + ViewModels
- Python state snapshots -> `AppSnapshotService`
- Python backup routines -> `BackupManager`
- Python settings checks -> `SettingsValidator`
- SQL naming heuristics -> `SqlDatabaseNameInference`
- File persistence -> JSON repositories in Infrastructure

## Risks and mitigation

1. UI parity risk: mitigated by one view model and view per section.
2. Data shape drift: mitigated by typed domain models and JSON serialization options.
3. Settings regressions: mitigated by validator unit tests.
4. Persistence regressions: mitigated by repository round-trip tests.

## Next migration increments

1. Add import adapters for legacy Python JSON payloads.
2. Add map provider and GPS live feed integration.
3. Replace JSON storage with SQL once schema is fixed.
4. Add CI pipeline for build and tests.
