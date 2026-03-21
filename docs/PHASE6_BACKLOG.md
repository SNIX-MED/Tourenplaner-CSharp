# Phase 6 Backlog (Prioritized)

## P0

1. Resolve full restore/build pipeline blocker (`MSB4276`)
   - goal: full `Tourenplaner.CSharp.sln` build and tests runnable locally/CI.
2. Map route drag interaction
   - files: `src/Tourenplaner.CSharp.App/Views/Sections/KarteSectionView.xaml.cs`, `.../KarteSectionViewModel.cs`
   - goal: reorder route stops directly via map interaction.

## P1

1. Map overlay and marker context expansion
   - status badges, richer popup actions, route ETA hints.
2. Additional tests for route persistence edges
   - files: `tests/Tourenplaner.CSharp.Tests/Application/*`
   - scenarios: duplicate stop handling, empty/invalid route save attempts, assigned-order updates.

## P2

1. Packaging and update pipeline alignment
   - publish profile, installer/update artifacts, release notes linkage.
2. UI parity polish pass
   - spacing/labels/layout fine-tuning against Python screens.
