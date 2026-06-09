@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "SOLUTION_PATH=%SCRIPT_DIR%Tourenplaner.CSharp\Tourenplaner.CSharp.sln"
set "RELEASE_LAUNCHER_PATH=%SCRIPT_DIR%Tourenplaner.CSharp\artifacts\publish\win-x64\GAWELA.Tourenplaner.exe"
set "DEBUG_LAUNCHER_PATH=%SCRIPT_DIR%Tourenplaner.CSharp\src\Tourenplaner.CSharp.Launcher\bin\Debug\net8.0-windows\GAWELA.Tourenplaner.exe"

if exist "%RELEASE_LAUNCHER_PATH%" (
    start "" "%RELEASE_LAUNCHER_PATH%"
    exit /b 0
)

if not exist "%SOLUTION_PATH%" (
    echo Die Solution wurde nicht gefunden:
    echo %SOLUTION_PATH%
    pause
    exit /b 1
)

echo Baue aktuelle Version mit Launcher...
dotnet build "%SOLUTION_PATH%" -v minimal -m:1 -nr:false -p:NuGetAudit=false
if errorlevel 1 (
    echo.
    echo Build fehlgeschlagen. Tourenplaner wird nicht gestartet.
    pause
    exit /b 1
)

if not exist "%DEBUG_LAUNCHER_PATH%" (
    echo Die Launcher-EXE wurde nicht gefunden:
    echo %DEBUG_LAUNCHER_PATH%
    echo.
    echo Bitte zuerst die Solution erfolgreich bauen.
    pause
    exit /b 1
)

start "" "%DEBUG_LAUNCHER_PATH%"
exit /b 0
