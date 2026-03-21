# C# Migration Plan

## Progress snapshot (Phase 5 ongoing)

Completed increments:
- solution and layered architecture setup (`App`, `Application`, `Domain`, `Infrastructure`, `Tests`)
- parity domain models + JSON normalizers/repositories
- backup, settings validation, SQL name inference core services
- tours scheduling/conflict services + unit tests
- WPF implementation for Start, Kalender, Tours, Orders, Non-Map Orders, Employees, Vehicles, Settings, GPS, Updates

Open major increments:
- direct cross-navigation interactions (Kalender -> Tours focus)
- full solution build/test run after resolving local SDK restore issue (`MSB4276`)

Phase 5 closure status:
- see `docs/PHASE5_COMPLETION_REPORT.md` for delivered scope and remaining gaps

## Immediate next implementation steps

1. Karte visual enhancement:
   - extend advanced map interactions (drag route edits, richer overlays, deeper marker actions)
   - keep fallback behavior when embedded map host is unavailable
2. Quality pass:
   - extend tests for order split persistence and settings backup workflows
   - execute full build/test after environment fix
