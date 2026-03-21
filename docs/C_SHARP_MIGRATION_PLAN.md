# C# Migration Plan

## Delivered baseline

Completed:
- layered architecture and solution structure
- parity models/repositories/services for primary workflows
- WPF MVVM sections for all requested navigation entries
- incremental parity hardening for tours, map, GPS, settings, backups, updates
- growing test suite for core application and repository behavior

## Current focus (next steps)

1. Map interaction hardening
   - extend overlays and advanced in-map route editing ergonomics
2. Quality/verification pass
   - add tests for map-route save/update edge cases
   - execute full solution build/test after resolving local restore blocker
3. Deployment readiness
   - define publish profile and packaging/update distribution flow

## Blocking item

- local environment restore/build blocker on full solution (`MSB4276`) affects App/Infrastructure/Tests restore path and prevents complete end-to-end CLI verification here.
