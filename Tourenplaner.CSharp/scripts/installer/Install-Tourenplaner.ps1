$ErrorActionPreference = "Stop"

$sourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appSource = Join-Path $sourceRoot "app"
$installRoot = Join-Path $env:LOCALAPPDATA "Programs\GAWELA Tourenplaner"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "GAWELA Tourenplaner.lnk"
$startMenuFolder = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs\GAWELA"
$startMenuShortcut = Join-Path $startMenuFolder "Tourenplaner.lnk"
$launcherPath = Join-Path $installRoot "GAWELA.Tourenplaner.exe"

if (-not (Test-Path $appSource)) {
    throw "Der Anwendungsordner wurde im Installer nicht gefunden."
}

New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
New-Item -ItemType Directory -Force -Path $startMenuFolder | Out-Null

Copy-Item -LiteralPath (Join-Path $appSource "*") -Destination $installRoot -Recurse -Force

$wshShell = New-Object -ComObject WScript.Shell

$desktopLink = $wshShell.CreateShortcut($desktopShortcut)
$desktopLink.TargetPath = $launcherPath
$desktopLink.WorkingDirectory = $installRoot
$desktopLink.IconLocation = $launcherPath
$desktopLink.Save()

$startMenuLink = $wshShell.CreateShortcut($startMenuShortcut)
$startMenuLink.TargetPath = $launcherPath
$startMenuLink.WorkingDirectory = $installRoot
$startMenuLink.IconLocation = $launcherPath
$startMenuLink.Save()

Start-Process -FilePath $launcherPath -WorkingDirectory $installRoot
