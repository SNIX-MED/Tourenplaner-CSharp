# C# Migration Plan

## Phase 1: Foundation

- Create .NET 8 solution and layered projects.
- Set common build rules via `Directory.Build.props`.
- Establish shared domain model contracts.

## Phase 2: Core logic

- Implement Application services:
  - `AppSnapshotService`
  - `BackupManager`
  - `SettingsValidator`
  - `SqlDatabaseNameInference`
- Define repository interfaces.

## Phase 3: Infrastructure

- Implement JSON repositories for Orders, Tours, Employees, Vehicles, and Settings.
- Use deterministic serializer settings for stable files.

## Phase 4: WPF UI

- Build shell navigation with sections:
  - Start, Kalender, Karte, GPS, Orders, Non-Map Orders, Tours,
    Employees, Vehicles, Settings, Updates.
- Bind each section to its own ViewModel and View.

## Phase 5: Quality

- Add unit tests for:
  - settings validation
  - SQL database name inference
  - JSON repository round-trip persistence
- Run `dotnet build` and `dotnet test`.
