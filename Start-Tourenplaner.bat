@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "EXE_PATH=%SCRIPT_DIR%Tourenplaner.CSharp\src\Tourenplaner.CSharp.App\bin\Debug\net8.0-windows\Tourenplaner.CSharp.App.exe"
set "LOG_PATH=%LOCALAPPDATA%\Tourenplaner.CSharp\data\app-crash.log"

if not exist "%EXE_PATH%" (
    echo Die App-EXE wurde nicht gefunden:
    echo %EXE_PATH%
    echo.
    echo Bitte zuerst das Projekt bauen.
    pause
    exit /b 1
)

start "" "%EXE_PATH%"
timeout /t 3 /nobreak >nul

tasklist /FI "IMAGENAME eq Tourenplaner.CSharp.App.exe" | find /I "Tourenplaner.CSharp.App.exe" >nul
if errorlevel 1 (
    echo.
    echo Die App wurde gestartet, ist aber sofort wieder beendet worden.
    if exist "%LOG_PATH%" (
        echo Crash-Log:
        echo %LOG_PATH%
    ) else (
        echo Es wurde kein Crash-Log gefunden.
    )
    echo.
    pause
    exit /b 2
)

exit /b 0
