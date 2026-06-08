param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$PublishedFilesRoot
)

$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path "$PSScriptRoot\..").Path
$publishedRoot = if ($PublishedFilesRoot) { $PublishedFilesRoot } else { Join-Path $projectRoot "artifacts\publish\$RuntimeIdentifier" }
$installerRoot = Join-Path $projectRoot "artifacts\installer\$RuntimeIdentifier"
$packageRoot = Join-Path $installerRoot "package"
$setupExePath = Join-Path $installerRoot "GAWELA-Tourenplaner-Setup.exe"
$sedPath = Join-Path $installerRoot "TourenplanerInstaller.sed"
$installCmdPath = Join-Path $packageRoot "Install-Tourenplaner.cmd"
$installPs1Source = Join-Path $PSScriptRoot "installer\Install-Tourenplaner.ps1"
$installPs1Target = Join-Path $packageRoot "Install-Tourenplaner.ps1"
$iexpressPath = Join-Path $env:WINDIR "System32\iexpress.exe"

if (-not (Test-Path $publishedRoot)) {
    throw "Die publizierten Dateien wurden nicht gefunden: $publishedRoot"
}

if (-not (Test-Path $installPs1Source)) {
    throw "Das Installationsskript fehlt: $installPs1Source"
}

if (-not (Test-Path $iexpressPath)) {
    throw "IExpress wurde nicht gefunden: $iexpressPath"
}

Remove-Item -Recurse -Force $installerRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null

Copy-Item -LiteralPath $installPs1Source -Destination $installPs1Target
New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot "app") | Out-Null
Copy-Item -Path (Join-Path $publishedRoot "*") -Destination (Join-Path $packageRoot "app") -Recurse -Force

@'
@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0Install-Tourenplaner.ps1"
exit /b %errorlevel%
'@ | Set-Content -Path $installCmdPath -Encoding ASCII

$appFiles = Get-ChildItem -Path (Join-Path $packageRoot "app") -Recurse -File | Sort-Object FullName
$sourceLines = New-Object System.Collections.Generic.List[string]
$sourceLines.Add("%FILE0%= ")
$sourceLines.Add("%FILE1%= ")
$stringLines = New-Object System.Collections.Generic.List[string]
$stringLines.Add("FILE0=Install-Tourenplaner.cmd")
$stringLines.Add("FILE1=Install-Tourenplaner.ps1")

$index = 2
foreach ($file in $appFiles) {
    $relativePath = $file.FullName.Substring($packageRoot.Length + 1)
    $sourceLines.Add("%FILE$index%= ")
    $stringLines.Add("FILE$index=$relativePath")
    $index++
}

$sedContent = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=Die Installation wurde abgeschlossen.
TargetName=$setupExePath
FriendlyName=GAWELA Tourenplaner Setup
AppLaunched=Install-Tourenplaner.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
[SourceFiles]
SourceFiles0=$packageRoot
[SourceFiles0]
$(($sourceLines -join [Environment]::NewLine))
[Strings]
$(($stringLines -join [Environment]::NewLine))
"@

Set-Content -Path $sedPath -Value $sedContent -Encoding ASCII

Write-Host "Erzeuge Installer..."
& $iexpressPath /N $sedPath | Out-Null

Write-Host ""
Write-Host "Fertig. Installer:"
Write-Host "  $setupExePath"
