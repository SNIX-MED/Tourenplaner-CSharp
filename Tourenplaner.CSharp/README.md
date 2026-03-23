# Tourenplaner.CSharp

.NET 8 WPF migration of the GAWELA Tourenplaner using a layered architecture.

## Solution layout

- `src/Tourenplaner.CSharp.App` - WPF shell, navigation, views, view models
- `src/Tourenplaner.CSharp.Application` - use cases, service logic, abstractions
- `src/Tourenplaner.CSharp.Domain` - core entities and value objects
- `src/Tourenplaner.CSharp.Infrastructure` - JSON repositories and persistence
- `tests/Tourenplaner.CSharp.Tests` - unit tests for services and repositories

## Start

```powershell
cd Tourenplaner.CSharp
dotnet build Tourenplaner.CSharp.sln
```

Run app:

```powershell
cd Tourenplaner.CSharp
dotnet run --project src/Tourenplaner.CSharp.App/Tourenplaner.CSharp.App.csproj
```

## SQL-Import

Der SQL-Import läuft in zwei Phasen (wie im Python-Projekt):

1. Phase 1: SQL lesen, deduplizieren, map/non-map trennen, bestehende Einträge aktualisieren, pending ohne Koordinaten persistieren.
2. Phase 2: pending im Hintergrund geocodieren (Fallback-Reihenfolge), Fortschritt in der UI anzeigen, Treffer übernehmen, Fehlschläge als pending belassen.

Konfiguration in `settings.json`:

- `SqlServerInstance` (Default `.\SQLEXPRESS`)
- `SqlDatabase` (optional, wird sonst aus größter `.mdf` in `SqlDataDir` abgeleitet)
- `SqlDataDir`

Persistierte Dateien im lokalen App-Datenordner (`%LOCALAPPDATA%\\Tourenplaner.CSharp\\data`):

- `pending_sql_orders.json`
- `non_map_sql_orders.json`
- `geocode_cache.json`
- `logs/sql_import_geocode_failed_*.txt` (nur bei Geocode-Fehlschlägen)

UI-Status:

- `Bereit`
- `Läuft seit HH:mm:ss`
- `Liste bereit. Geocoding läuft x/y`
- `Fertig: gelesen ..., neu ..., aktualisiert ..., geocodiert ..., offen ohne Koordinaten ...`
- `Fehler: ...`
