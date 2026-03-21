# Release Readiness Checkpoint (Phase 5)

Stand: 21.03.2026

## Ziel dieses Checkpoints
- Bewertbarer Status fuer den aktuellen Migrationsstand.
- Fokus auf Map-Route Kernlogik (`Save`, `Swap`, `Move`) und deren Testhaertung.

## Neu umgesetzt in diesem Schritt
- Neuer Application-Service: `MapRouteService`
  - Datei: `src/Tourenplaner.CSharp.Application/Services/MapRouteService.cs`
  - Verantwortet:
    - Route-Swap nach `OrderId`
    - Route-Move (Delta-basiert) mit Bounds-Checks
    - `nextTourId`-Ableitung
    - Tour-Erzeugung aus aktueller Route mit robusten Fallbacks
    - Extraktion der betroffenen `OrderId`s fuer Assignment-Updates
- Neuer Application-Datentyp: `MapRouteStop`
  - Datei: `src/Tourenplaner.CSharp.Application/Common/MapRouteStop.cs`
- Karte-UI an Application-Service gekoppelt
  - Datei: `src/Tourenplaner.CSharp.App/ViewModels/Sections/KarteSectionViewModel.cs`
  - Ergebnis:
    - `SwapRouteStops` nutzt Application-Service
    - `MoveSelectedStop` nutzt Application-Service
    - `SaveRouteAsTourAsync` nutzt Application-Service fuer Tour-Build + IDs

## Testhaertung (neu)
- Neue Testklasse:
  - `tests/Tourenplaner.CSharp.Tests/Application/MapRouteServiceTests.cs`
- Abgedeckte Edgecases:
  - `Swap`: gueltiger Tausch + Reindex
  - `Swap`: unbekannte IDs / gleiche IDs
  - `Move`: gueltige Verschiebung + Out-of-Bounds Schutz
  - `Save`: Fallbacks fuer Name/Datum/Uhrzeit + negative ServiceMinutes
  - `Save`: Fehlerfall bei leerer Route
  - `nextTourId`: leere Liste + nicht-sequenzielle IDs

## Verifikation
- Ausgefuehrt:
  - `dotnet test tests/Tourenplaner.CSharp.Tests/Tourenplaner.CSharp.Tests.csproj -c Debug /m:1`
- Ergebnis:
  - 28/28 Tests erfolgreich
  - 0 Fehler, 0 Uebersprungen

## Aktueller Readiness-Status
- Buildbarkeit: Gruen (lokal verifiziert).
- Kernlogik Route-Interaktion: In Application-Layer verankert und getestet.
- Migration: Weiterhin nicht vollstaendig feature-paritaet (Details in Backlog und Phase-Reports).

## Offene Risiken vor Release
- Vollstaendige End-to-End UI-Paritaet gegen Python noch nicht final abgeschlossen.
- Kartenintegration/Interaktion benoetigt weiterhin reale User-Abnahme (nicht nur Unit-Tests).
- Paketierung/Installer/Update-Kanal muss abschliessend gegen Zielumgebung verifiziert werden.

## Exit-Kriterien fuer naechsten Checkpoint
- Abnahme der verbleibenden Feature-Paritaets-Punkte aus `docs/PHASE6_BACKLOG.md`.
- Smoke-Test der Hauptseiten (Start, Kalender, Karte, GPS, Orders, Tours, Settings, Updates).
- Release-Build und Packaging-Lauf mit dokumentierten Ergebnissen.
