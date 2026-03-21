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
- explicit embedded WebView2 control integration (currently fallback-ready)
- full solution build/test run after resolving local SDK restore issue (`MSB4276`)

## Immediate next implementation steps

1. Karte visual enhancement:
   - add embedded map tile host and marker rendering for full visual parity
   - keep current route panel as fallback when map host is unavailable
2. Kalender interaction enhancement:
   - add action to open selected day/tour directly in Tours context
3. Quality pass:
   - extend tests for order split persistence and settings backup workflows
   - execute full build/test after environment fix
