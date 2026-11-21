@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "SOLUTION_PATH=%SCRIPT_DIR%Tourenplaner.CSharp\Tourenplaner.CSharp.sln"
set "DEBUG_APP_PATH=%SCRIPT_DIR%Tourenplaner.CSharp\src\Tourenplaner.CSharp.App\bin\Debug\net8.0-windows\Tourenplaner.CSharp.App.exe"
set "BUILD_LOG_1=%TEMP%\gawela-preview-build-%RANDOM%-%RANDOM%-1.log"
set "BUILD_LOG_2=%TEMP%\gawela-preview-build-%RANDOM%-%RANDOM%-2.log"

if not exist "%SOLUTION_PATH%" (
    echo Die Solution wurde nicht gefunden:
    echo %SOLUTION_PATH%
    pause
    exit /b 1
)

rem Eine laufende Vorschau kann noch den alten Programmstand anzeigen und
rem gleichzeitig Builddateien sperren. Vor jedem Neubau daher gezielt beenden.
tasklist /FI "IMAGENAME eq Tourenplaner.CSharp.App.exe" 2>NUL | find /I "Tourenplaner.CSharp.App.exe" >NUL
if not errorlevel 1 (
    echo Beende laufende Vorschauversion...
    taskkill /F /IM Tourenplaner.CSharp.App.exe >NUL 2>&1
    timeout /t 1 /nobreak >NUL
)

echo Baue lokale Vorschauversion...
rem Rebuild bereinigt WPF-Zwischendateien ueber MSBuild in der korrekten Reihenfolge.
rem Falls WPF eine generierte Datei erst waehrend dieses Laufs vorbereitet, wird
rem automatisch ein zweiter inkrementeller Build ausgefuehrt.
dotnet build "%SOLUTION_PATH%" --no-restore -t:Rebuild -v minimal -m:1 -nr:false -p:NuGetAudit=false > "%BUILD_LOG_1%" 2>&1
if errorlevel 1 (
    echo WPF-Dateien werden vervollstaendigt, Build wird automatisch wiederholt...
    dotnet build "%SOLUTION_PATH%" --no-restore -v minimal -m:1 -nr:false -p:NuGetAudit=false > "%BUILD_LOG_2%" 2>&1
    if errorlevel 1 (
        echo.
        type "%BUILD_LOG_1%"
        type "%BUILD_LOG_2%"
        del /q "%BUILD_LOG_1%" "%BUILD_LOG_2%" >NUL 2>&1
        echo.
        echo Build fehlgeschlagen. Vorschau wird nicht gestartet.
        pause
        exit /b 1
    )
    type "%BUILD_LOG_2%"
) else (
    type "%BUILD_LOG_1%"
)
del /q "%BUILD_LOG_1%" "%BUILD_LOG_2%" >NUL 2>&1

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
