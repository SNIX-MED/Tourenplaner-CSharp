param(
    [string]$ProjectRoot = (Resolve-Path "$PSScriptRoot\..").Path
)

$ErrorActionPreference = "Stop"

$exePath = Join-Path $ProjectRoot "src\Tourenplaner.CSharp.App\bin\Debug\net8.0-windows\Tourenplaner.CSharp.App.exe"

if (-not (Test-Path $exePath)) {
    throw "App executable not found: $exePath"
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
namespace Win32
{
    public static class NativeMethods
    {
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
"@

function Show-And-FocusWindow {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process) { return $false }
    if ($Process.HasExited) { return $false }

    $handle = $Process.MainWindowHandle
    if ($handle -eq 0) { return $false }

    # SW_RESTORE = 9
    [Win32.NativeMethods]::ShowWindowAsync($handle, 9) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win32.NativeMethods]::SetForegroundWindow($handle) | Out-Null
    return $true
}

$running = Get-Process -Name "Tourenplaner.CSharp.App" -ErrorAction SilentlyContinue
if ($running) {
    foreach ($proc in $running) {
        if (Show-And-FocusWindow -Process $proc) {
            Write-Output "Focused existing Tourenplaner window (PID $($proc.Id))."
            exit 0
        }
    }

    # No visible window found (stale/background process). Clean up and relaunch.
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 200
}

$started = Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath) -PassThru

for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 200
    try {
        $started.Refresh()
    }
    catch {
        break
    }

    if (Show-And-FocusWindow -Process $started) {
        Write-Output "Started and focused Tourenplaner window (PID $($started.Id))."
        exit 0
    }
}

Write-Output "Started process (PID $($started.Id)), but no window handle detected yet."
