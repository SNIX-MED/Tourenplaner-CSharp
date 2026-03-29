@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT_PATH=%SCRIPT_DIR%Tourenplaner.CSharp\src\Tourenplaner.CSharp.App\Tourenplaner.CSharp.App.csproj"
set "EXE_PATH=%SCRIPT_DIR%Tourenplaner.CSharp\src\Tourenplaner.CSharp.App\bin\Debug\net8.0-windows\Tourenplaner.CSharp.App.exe"
set "LOG_PATH=%LOCALAPPDATA%\Tourenplaner.CSharp\data\app-crash.log"

if not exist "%PROJECT_PATH%" (
    echo Die Projektdatei wurde nicht gefunden:
    echo %PROJECT_PATH%
    pause
    exit /b 1
)

tasklist /FI "IMAGENAME eq Tourenplaner.CSharp.App.exe" | find /I "Tourenplaner.CSharp.App.exe" >nul
if %errorlevel%==0 (
    echo Die App laeuft bereits. Bitte vorhandenes Fenster verwenden oder die App zuerst schliessen.
    pause
    exit /b 0
)

echo Baue aktuelle Version...
dotnet build "%PROJECT_PATH%" -v minimal -m:1 -nr:false -p:NuGetAudit=false
if errorlevel 1 (
    echo.
    echo Build fehlgeschlagen. App wird nicht gestartet.
    pause
    exit /b 1
)

if not exist "%EXE_PATH%" (
    echo Die App-EXE wurde nicht gefunden:
    echo %EXE_PATH%
    echo.
    echo Bitte zuerst das Projekt bauen.
    pause
    exit /b 1
)

start "" "%EXE_PATH%"
exit /b 0
