@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "SOLUTION_PATH=%SCRIPT_DIR%Tourenplaner.CSharp\Tourenplaner.CSharp.sln"
set "DEBUG_APP_PATH=%SCRIPT_DIR%Tourenplaner.CSharp\src\Tourenplaner.CSharp.App\bin\Debug\net8.0-windows\Tourenplaner.CSharp.App.exe"

if not exist "%SOLUTION_PATH%" (
    echo Die Solution wurde nicht gefunden:
    echo %SOLUTION_PATH%
    pause
    exit /b 1
)

echo Baue lokale Vorschauversion...
dotnet build "%SOLUTION_PATH%" -v minimal -m:1 -nr:false -p:NuGetAudit=false
if errorlevel 1 (
    echo.
    echo Build fehlgeschlagen. Vorschau wird nicht gestartet.
    pause
    exit /b 1
)

if not exist "%DEBUG_APP_PATH%" (
    echo Die Vorschau-EXE wurde nicht gefunden:
    echo %DEBUG_APP_PATH%
    echo.
    echo Bitte zuerst die Solution erfolgreich bauen.
    pause
    exit /b 1
)

start "" "%DEBUG_APP_PATH%"
exit /b 0
