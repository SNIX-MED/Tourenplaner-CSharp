# C# Migration Analysis

## Current target state

The .NET 8 implementation provides an end-to-end functional migration baseline across all required navigation areas:
- Start
- Kalender
- Karte
- GPS
- Orders
- Non-Map Orders
- Tours
- Employees
- Vehicles
- Settings
- Updates

Core data, scheduling, backup, conflict and settings logic are implemented in layered services/repositories and wired into WPF MVVM sections.

## Parity snapshot

Implemented:
- functional shell/navigation parity with dedicated ViewModel/View per section
- parity-focused JSON repositories + normalization strategy
- tours scheduling and assignment conflict detection
- map route workflow including embedded tile host, marker sync, route polyline and tour save-back
- GPS embedded WebView2 + browser fallback behavior
- settings + backup/restore + validation operations
- updates/status view with runtime/version/backup info
- calendar month/day/tour view with direct navigation into Tours section

Partially complete:
- advanced map UX parity: drag-based route editing and richer overlays (beyond current marker/panel/route polyline set)
- Windows packaging/update distribution parity (AppInstaller/MSIX pipeline level)

Environment limitation:
- full solution restore/build/test in this environment remains blocked by local SDK/workload resolver issue (`MSB4276`) on App/Infrastructure/Test restore path.
- Domain/Application compile successfully.

## Risks and mitigation

1. Advanced map parity drift:
   - mitigate by incremental WebView2 map interaction backlog (drag route reorder, contextual overlays).
2. Environment-specific regressions:
   - mitigate via CI build/test in clean Windows agents once restore blocker is removed.
3. Data compatibility regressions:
   - mitigate via expanding repository + service tests around split/merge and normalization.

## Next migration increments

1. Map interaction hardening:
   - drag reorder and richer marker actions in embedded map.
2. Test hardening:
   - extend coverage for map-route save/update edge-cases.
3. Build pipeline hardening:
   - resolve restore blocker and run full `sln` build + tests.
4. Packaging:
   - publish profile and installer/update flow alignment.
