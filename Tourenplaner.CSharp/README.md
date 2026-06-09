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

## Windows EXE / Installer

Direkt startbare Windows-EXE erzeugen:

```powershell
./scripts/publish-windows.ps1
```

Das Ergebnis liegt danach unter `artifacts/publish/win-x64/GAWELA.Tourenplaner.exe`.

Installer-EXE erzeugen:

```powershell
./scripts/publish-windows.ps1 -BuildInstaller
```

Das Setup wird unter `artifacts/installer/win-x64/GAWELA-Tourenplaner-Setup.exe` abgelegt.

Der Installer wird mit Inno Setup 6 gebaut und bietet:

- frei waehlbaren Installationspfad
- optionale Desktop-Verknuepfung
- optionale Startmenue-Verknuepfung
- Deinstallation ueber Windows
- automatische Update-Pruefung beim Start ueber `update-config.json` und `update-manifest.json`

Vor dem Build des Installers muss `Inno Setup 6` auf dem Build-Rechner installiert sein.

Fuer automatische Updates wird beim Publish eine `update-config.json` erzeugt, sofern das Git-Remote auf GitHub zeigt oder `-ReleaseBaseUrl` gesetzt ist. Beim Installer-Build entsteht zusaetzlich `artifacts/installer/win-x64/update-manifest.json`.

Fuer einen Release muessen mindestens diese Dateien in denselben GitHub-Release hochgeladen werden:

- `GAWELA-Tourenplaner-Setup.exe`
- `update-manifest.json`

Optional kann die Release-Basis explizit gesetzt werden:

```powershell
./scripts/publish-windows.ps1 -BuildInstaller -ReleaseBaseUrl "https://github.com/<owner>/<repo>/releases/latest/download"
```

Bei einer neuen Version muss `Version` in `Directory.Build.props` erhoeht werden.

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
