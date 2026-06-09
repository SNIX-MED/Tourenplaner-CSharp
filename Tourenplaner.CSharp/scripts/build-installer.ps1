param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$PublishedFilesRoot,
    [string]$AppVersion,
    [string]$ReleaseBaseUrl
)

$ErrorActionPreference = "Stop"

function Get-InnoSetupCompilerPath {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }

    if ($candidates.Count -gt 0) {
        return [string]$candidates[0]
    }

    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command -and $command.Source -and (Test-Path $command.Source)) {
        return [string]$command.Source
    }

    throw "Inno Setup 6 wurde nicht gefunden. Bitte installieren Sie Inno Setup 6, damit der Installer gebaut werden kann."
}

function New-InstallerBitmap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath,
        [Parameter(Mandatory = $true)]
        [int]$Width,
        [Parameter(Mandatory = $true)]
        [int]$Height,
        [string]$BannerPath,
        [string]$LogoPath,
        [string]$BackgroundHex = "#F6F1E8"
    )

    Add-Type -AssemblyName System.Drawing

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $background = [System.Drawing.ColorTranslator]::FromHtml($BackgroundHex)
    $graphics.Clear($background)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    try {
        if ($BannerPath -and (Test-Path $BannerPath)) {
            $banner = [System.Drawing.Image]::FromFile($BannerPath)
            try {
                $targetWidth = [Math]::Min($Width - 24, $banner.Width)
                $scale = $targetWidth / $banner.Width
                $targetHeight = [Math]::Max([int]($banner.Height * $scale), 1)
                $x = [int](($Width - $targetWidth) / 2)
                $y = if ($Height -ge 200) { 18 } else { 10 }
                $graphics.DrawImage($banner, $x, $y, $targetWidth, $targetHeight)
            }
            finally {
                $banner.Dispose()
            }
        }

        if ($LogoPath -and (Test-Path $LogoPath)) {
            $logo = [System.Drawing.Image]::FromFile($LogoPath)
            try {
                $maxWidth = if ($Height -ge 200) { [int]($Width * 0.72) } else { [int]($Width * 0.78) }
                $maxHeight = if ($Height -ge 200) { [int]($Height * 0.34) } else { [int]($Height * 0.72) }
                $scale = [Math]::Min($maxWidth / $logo.Width, $maxHeight / $logo.Height)
                $targetWidth = [Math]::Max([int]($logo.Width * $scale), 1)
                $targetHeight = [Math]::Max([int]($logo.Height * $scale), 1)
                $x = [int](($Width - $targetWidth) / 2)
                $y = if ($Height -ge 200) {
                    [int](($Height - $targetHeight) * 0.58)
                }
                else {
                    [int](($Height - $targetHeight) / 2)
                }

                $graphics.DrawImage($logo, $x, $y, $targetWidth, $targetHeight)
            }
            finally {
                $logo.Dispose()
            }
        }

        $bitmap.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Resolve-CleanVersion {
    param(
        [string]$VersionText
    )

    if ([string]::IsNullOrWhiteSpace($VersionText)) {
        return $null
    }

    $normalizedVersion = $VersionText.Trim()
    $plusIndex = $normalizedVersion.IndexOf('+')
    if ($plusIndex -ge 0) {
        $normalizedVersion = $normalizedVersion.Substring(0, $plusIndex)
    }

    if ($normalizedVersion -match '^\d+\.\d+\.\d+\.0$') {
        $normalizedVersion = $normalizedVersion.Substring(0, $normalizedVersion.Length - 2)
    }

    return $normalizedVersion.Trim()
}

$projectRoot = (Resolve-Path "$PSScriptRoot\..").Path
$publishedRoot = if ($PublishedFilesRoot) { (Resolve-Path $PublishedFilesRoot).Path } else { Join-Path $projectRoot "artifacts\publish\$RuntimeIdentifier" }
$installerRoot = Join-Path $projectRoot "artifacts\installer\$RuntimeIdentifier"
$brandingRoot = Join-Path $installerRoot "branding"
$issTemplatePath = Join-Path $PSScriptRoot "installer\TourenplanerInstaller.iss"
$launcherPath = Join-Path $publishedRoot "GAWELA.Tourenplaner.exe"
$wizardImagePath = Join-Path $brandingRoot "wizard.bmp"
$wizardSmallImagePath = Join-Path $brandingRoot "wizard-small.bmp"
$bannerPath = Join-Path $projectRoot "src\Tourenplaner.CSharp.App\Assets\Banner.png"
$logoPath = Join-Path $projectRoot "src\Tourenplaner.CSharp.App\Assets\Applogo.png"
$isccPath = Get-InnoSetupCompilerPath
$updateManifestPath = Join-Path $installerRoot "update-manifest.json"

if (-not (Test-Path $publishedRoot)) {
    throw "Die publizierten Dateien wurden nicht gefunden: $publishedRoot"
}

if (-not (Test-Path $launcherPath)) {
    throw "Die Startdatei fuer den Installer wurde nicht gefunden: $launcherPath"
}

if (-not (Test-Path $issTemplatePath)) {
    throw "Die Inno-Setup-Vorlage fehlt: $issTemplatePath"
}

$resolvedAppVersion = $AppVersion
if (-not $resolvedAppVersion) {
    $versionInfo = (Get-Item $launcherPath).VersionInfo
    $resolvedAppVersion = if ([string]::IsNullOrWhiteSpace($versionInfo.ProductVersion)) {
        if ([string]::IsNullOrWhiteSpace($versionInfo.FileVersion)) { "1.0.0" } else { $versionInfo.FileVersion }
    }
    else {
        $versionInfo.ProductVersion
    }
}

$resolvedAppVersion = Resolve-CleanVersion -VersionText $resolvedAppVersion
if ([string]::IsNullOrWhiteSpace($resolvedAppVersion)) {
    $resolvedAppVersion = "1.0.0"
}

Remove-Item -Recurse -Force $installerRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $brandingRoot | Out-Null

New-InstallerBitmap -DestinationPath $wizardImagePath -Width 164 -Height 314 -BannerPath $bannerPath -LogoPath $logoPath
New-InstallerBitmap -DestinationPath $wizardSmallImagePath -Width 55 -Height 55 -LogoPath $logoPath

$isccArguments = @(
    "/Qp",
    "/DPublishedFilesRoot=$publishedRoot",
    "/DInstallerRoot=$installerRoot",
    "/DAppVersion=$resolvedAppVersion",
    "/DWizardImageFile=$wizardImagePath",
    "/DWizardSmallImageFile=$wizardSmallImagePath",
    $issTemplatePath
)

Write-Host "Erzeuge Installer mit Inno Setup..."
$compilerProcess = Start-Process -FilePath $isccPath -ArgumentList $isccArguments -Wait -PassThru -NoNewWindow

if ($compilerProcess.ExitCode -ne 0) {
    throw "Inno Setup konnte den Installer nicht erzeugen."
}

$setupPath = Join-Path $installerRoot "GAWELA-Tourenplaner-Setup.exe"
if ($ReleaseBaseUrl -and (Test-Path $setupPath)) {
    $hash = Get-FileHash -Path $setupPath -Algorithm SHA256
    @{
        version = $resolvedAppVersion
        installerUrl = "$($ReleaseBaseUrl.TrimEnd('/'))/GAWELA-Tourenplaner-Setup.exe"
        sha256 = $hash.Hash.ToLowerInvariant()
        publishedAtUtc = [DateTime]::UtcNow.ToString("O")
    } | ConvertTo-Json | Set-Content -Path $updateManifestPath -Encoding UTF8
}

Write-Host ""
Write-Host "Fertig. Installer:"
Write-Host "  $setupPath"

if (Test-Path $updateManifestPath) {
    Write-Host "Update-Manifest:"
    Write-Host "  $updateManifestPath"
}
