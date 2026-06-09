# Tourenplaner-CSharp

Dieses Repository enthaelt den GAWELA Tourenplaner als .NET-8-WPF-Anwendung.

Das eigentliche Projekt liegt im Unterordner `Tourenplaner.CSharp`.

## Schnellstart

Lokale Vorschau der unveroeffentlichten Version:

```bat
Start-Vorschau.bat
```

Lokalen zuletzt publizierten Release-Build starten:

```bat
Start-Releaseversion.bat
```

Alternativ per Terminal:

```powershell
cd Tourenplaner.CSharp
dotnet build Tourenplaner.CSharp.sln
dotnet run --project src/Tourenplaner.CSharp.App/Tourenplaner.CSharp.App.csproj
```

## Wichtige Dateien

- `Start-Vorschau.bat` startet immer die lokale Entwicklungs-/Vorschauversion.
- `Start-Releaseversion.bat` startet bevorzugt den zuletzt erzeugten Publish-Build.
- `Tourenplaner.CSharp\README.md` enthaelt die ausfuehrliche Projekt-, Installer- und Release-Dokumentation.

## Installer und Updates

Ein Windows-Installer wird aus dem Projektordner gebaut:

```powershell
cd Tourenplaner.CSharp
./scripts/publish-windows.ps1 -BuildInstaller
```

Die erzeugten Dateien liegen danach unter:

- `Tourenplaner.CSharp\artifacts\installer\win-x64\GAWELA-Tourenplaner-Setup.exe`
- `Tourenplaner.CSharp\artifacts\installer\win-x64\update-manifest.json`

Der Installer:

- bietet einen grafischen Setup-Assistenten
- erlaubt die Wahl des Installationspfads
- kann Desktop- und Startmenu-Verknuepfungen erstellen
- installiert eine Version mit automatischer Update-Pruefung beim Start

## Weitere Details

Die vollstaendige technische Dokumentation befindet sich in [Tourenplaner.CSharp/README.md](C:/Users/Verkauf_OG/Desktop/Tourenplaner-CSharp/Tourenplaner.CSharp/README.md).
