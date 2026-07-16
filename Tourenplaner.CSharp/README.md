# Tourenplaner.CSharp

Der GAWELA Tourenplaner ist hier als .NET-8-WPF-Anwendung mit Layered Architecture umgesetzt.

## Projektstruktur

- `src/Tourenplaner.CSharp.App` - WPF-Oberflaeche, Views, ViewModels, Dialoge
- `src/Tourenplaner.CSharp.Application` - Anwendungslogik, Services, Abstraktionen
- `src/Tourenplaner.CSharp.Domain` - Fachobjekte und Kernmodelle
- `src/Tourenplaner.CSharp.Infrastructure` - JSON-Repositories, Persistenz, externe Dienste
- `src/Tourenplaner.CSharp.Launcher` - Start-Launcher mit automatischer Update-Pruefung
- `tests/Tourenplaner.CSharp.Tests` - Unit-Tests

## Voraussetzungen

- Windows
- .NET 8 SDK
- optional fuer den Installer-Build: `Inno Setup 6`

## Qualitaetsstandards

- Nullable Reference Types und Implicit Usings sind global aktiviert.
- Compiler-Warnungen werden standardmaessig als Fehler behandelt.
- Ausnahme: `NU1900` bleibt als nicht blockierende Paket-Metadatenwarnung zugelassen.
- Formatierung und Zeilenenden werden ueber `.editorconfig` vereinheitlicht.

## Lokale Entwicklung

Solution bauen:

```powershell
dotnet build Tourenplaner.CSharp.sln
```

Formatierung pruefen:

```powershell
dotnet format whitespace Tourenplaner.CSharp.sln --verify-no-changes
```

Tests ausfuehren:

```powershell
dotnet test Tourenplaner.CSharp.sln
```

App direkt starten:

```powershell
dotnet run --project src/Tourenplaner.CSharp.App/Tourenplaner.CSharp.App.csproj
```

Lokale Vorschau ueber Batch-Datei:

```bat
..\Start-Vorschau.bat
```

## Versionierung

Die sichtbare Programmversion wird zentral in `Directory.Build.props` gepflegt:

- `Version`
- `AssemblyVersion`
- `FileVersion`
- `InformationalVersion`

Wenn eine neue Release-Version gebaut wird, muss diese Datei entsprechend aktualisiert werden.

## Windows-Release bauen

Direkt startbare Release-EXE erzeugen:

```powershell
./scripts/publish-windows.ps1
```

Ergebnis:

- `artifacts/publish/win-x64/GAWELA.Tourenplaner.exe`

Diese Version ist fuer lokale Release-Tests gedacht, nicht fuer die Endanwender-Installation.

## Windows-Installer bauen

Installer inkl. Update-Manifest erzeugen:

```powershell
./scripts/publish-windows.ps1 -BuildInstaller
```

Ergebnis:

- `artifacts/installer/win-x64/GAWELA-Tourenplaner-Setup.exe`
- `artifacts/installer/win-x64/update-manifest.json`

Der Installer bietet:

- grafischen Setup-Assistenten
- frei waehlbaren Installationspfad
- optionale Desktop-Verknuepfung
- optionale Startmenu-Verknuepfung
- Deinstallation ueber Windows

## Installation auf einem Ziel-PC

Auf dem Ziel-PC wird nur diese Datei benoetigt:

- `GAWELA-Tourenplaner-Setup.exe`

Installation:

1. `GAWELA-Tourenplaner-Setup.exe` auf den Ziel-PC kopieren.
2. Setup starten.
3. Installationspfad waehlen.
4. Optional Desktop- und Startmenu-Verknuepfung aktivieren.
5. Installation abschliessen.

## Automatische Updates

Die installierte Version prueft beim Start automatisch, ob eine neuere Version verfuegbar ist.

Dafuer werden zwei Dateien verwendet:

- `update-config.json` im Publish-Build
- `update-manifest.json` im GitHub-Release

Beim Start passiert Folgendes:

1. Der Launcher liest `update-config.json`.
2. Er laedt `update-manifest.json` aus GitHub.
3. Wenn eine neuere Version verfuegbar ist, wird `GAWELA-Tourenplaner-Setup.exe` automatisch heruntergeladen.
4. Das Update wird im Hintergrund installiert.
5. Anschliessend startet der Launcher die aktualisierte Version neu.

Wichtig:

- Alte Installationen ohne den neuen Launcher aktualisieren sich nicht selbst.
- In diesem Fall muss einmal manuell die neue Setup-Datei installiert werden.
- Ab dieser neuen Version funktionieren spaetere Updates automatisch.

## Release auf GitHub

Die aktuelle Release-Struktur nutzt GitHub Releases.

Mindestens diese beiden Dateien muessen in denselben Release hochgeladen werden:

- `GAWELA-Tourenplaner-Setup.exe`
- `update-manifest.json`

Die aktuelle Release-Basis ist auf GitHub konfiguriert als:

`https://github.com/SNIX-MED/Tourenplaner-CSharp/releases/latest/download`

## Startdateien im Repository

Im Repository-Stamm liegen zwei Batch-Dateien:

- `..\Start-Vorschau.bat` startet immer die lokale unveroeffentlichte Entwicklungsfassung
- `..\Start-Releaseversion.bat` startet bevorzugt den zuletzt erzeugten Publish-Build

## SQL-Import

Der SQL-Import laeuft zweistufig:

1. Daten lesen, deduplizieren, map/non-map trennen und persistieren
2. fehlende Koordinaten im Hintergrund geocodieren

Konfiguration in `settings.json`:

- `SqlServerInstance`
- `SqlDatabase`
- `SqlDataDir`

Persistierte Daten im lokalen App-Datenordner:

- `%LOCALAPPDATA%\Tourenplaner.CSharp\data\pending_sql_orders.json`
- `%LOCALAPPDATA%\Tourenplaner.CSharp\data\non_map_sql_orders.json`
- `%LOCALAPPDATA%\Tourenplaner.CSharp\data\geocode_cache.json`
- `%LOCALAPPDATA%\Tourenplaner.CSharp\data\logs\sql_import_geocode_failed_*.txt`

## TomTom API Key

Standardmaessig wird kein TomTom-Key mehr im Repository mitgeliefert.
Der Key kann lokal in den Einstellungen gespeichert oder ueber die Umgebungsvariable
`TOURENPLANER_TOMTOM_API_KEY` bereitgestellt werden.

## Nuetzliche Befehle

Build:

```powershell
dotnet build Tourenplaner.CSharp.sln
```

Tests:

```powershell
dotnet test Tourenplaner.CSharp.sln
```

Release-EXE bauen:

```powershell
./scripts/publish-windows.ps1
```

Installer bauen:

```powershell
./scripts/publish-windows.ps1 -BuildInstaller
```
