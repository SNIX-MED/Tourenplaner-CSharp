# C# Migration Plan

## Progress snapshot (Phase 5 ongoing)

Completed increments:
- solution and layered architecture setup (`App`, `Application`, `Domain`, `Infrastructure`, `Tests`)
- parity domain models + JSON normalizers/repositories
- backup, settings validation, SQL name inference core services
- tours scheduling/conflict services + unit tests
- WPF implementation for Start, Kalender, Tours, Orders, Non-Map Orders, Employees, Vehicles, Settings, GPS, Updates

Open major increments:
- Karte section functional parity (marker/filter/search/route panel/actions)
- direct cross-navigation interactions (Kalender -> Tours focus)
- explicit embedded WebView2 control integration (currently fallback-ready)
- full solution build/test run after resolving local SDK restore issue (`MSB4276`)

## Immediate next implementation steps

1. Karte migration:
   - adopt map/order interaction patterns from Python map flow
   - implement marker list and order filter/search panel
   - integrate route detail panel with stop actions
2. Kalender interaction enhancement:
   - add action to open selected day/tour directly in Tours context
3. Quality pass:
   - extend tests for order split persistence and settings backup workflows
   - execute full build/test after environment fix
