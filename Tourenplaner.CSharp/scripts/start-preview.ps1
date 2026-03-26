param(
    [string]$ProjectRoot = (Resolve-Path "$PSScriptRoot\..").Path
)

$ErrorActionPreference = "Stop"

$exePath = Join-Path $ProjectRoot "src\Tourenplaner.CSharp.App\bin\Debug\net8.0-windows\Tourenplaner.CSharp.App.exe"
$currentSessionId = (Get-Process -Id $PID).SessionId

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
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    }
}
"@

function Wait-ForMainWindow {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutMilliseconds = 15000
    )

    if ($null -eq $Process) { return $null }

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            if ($Process.HasExited) { return $null }
            $Process.Refresh()
        }
        catch {
            return $null
        }

        if ($Process.MainWindowHandle -ne 0 -and [Win32.NativeMethods]::IsWindowVisible($Process.MainWindowHandle)) {
            return $Process
        }

        Start-Sleep -Milliseconds 200
    }

    return $null
}

function Show-And-FocusWindow {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutMilliseconds = 15000
    )

    if ($null -eq $Process) { return $false }
    if ($Process.HasExited) { return $false }

    $Process = Wait-ForMainWindow -Process $Process -TimeoutMilliseconds $TimeoutMilliseconds
    if ($null -eq $Process) { return $false }

    $handle = $Process.MainWindowHandle
    if ($handle -eq 0) { return $false }

    # SW_RESTORE = 9
    [Win32.NativeMethods]::ShowWindowAsync($handle, 9) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win32.NativeMethods]::SetForegroundWindow($handle) | Out-Null
    return $true
}

$running = Get-Process -Name "Tourenplaner.CSharp.App" -ErrorAction SilentlyContinue |
    Where-Object { $_.SessionId -eq $currentSessionId }

if ($running) {
    foreach ($proc in $running) {
        if (Show-And-FocusWindow -Process $proc -TimeoutMilliseconds 2000) {
            Write-Output "Focused existing Tourenplaner window (PID $($proc.Id))."
            exit 0
        }
    }

    # No visible window found in the current interactive session. Clean up and relaunch.
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

$started = Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath) -PassThru

if (Show-And-FocusWindow -Process $started -TimeoutMilliseconds 20000) {
    Write-Output "Started and focused Tourenplaner window (PID $($started.Id), Session $($started.SessionId))."
    exit 0
}

try {
    $started.Refresh()
}
catch {
}

if ($started.HasExited) {
    throw "Tourenplaner exited before opening a main window. ExitCode: $($started.ExitCode)"
}

try {
    $started | Stop-Process -Force
}
catch {
}

throw "Tourenplaner started (PID $($started.Id)) but did not create a visible main window within 20 seconds."
