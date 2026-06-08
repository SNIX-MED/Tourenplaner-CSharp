# Tourenplaner-CSharp

Das eigentliche .NET-Projekt liegt im Unterordner `Tourenplaner.CSharp`.

Windows-Release mit Start-EXE erzeugen:

```powershell
cd Tourenplaner.CSharp
./scripts/publish-windows.ps1
```

Optional direkt einen Installer als `Setup.exe` bauen:

```powershell
cd Tourenplaner.CSharp
./scripts/publish-windows.ps1 -BuildInstaller
```

Vorschau starten:

```powershell
./scripts/start-preview.ps1
```

Alternativ direkt aus dem Projektordner:

```powershell
cd Tourenplaner.CSharp
dotnet run --project src/Tourenplaner.CSharp.App/Tourenplaner.CSharp.App.csproj
```
