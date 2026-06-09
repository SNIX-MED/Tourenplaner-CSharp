param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$BuildInstaller,
    [string]$ReleaseBaseUrl
)

$ErrorActionPreference = "Stop"

function Get-GitHubReleaseBaseUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $remoteUrl = (& git -C $RepositoryRoot remote get-url origin 2>$null)
    if ([string]::IsNullOrWhiteSpace($remoteUrl)) {
        return $null
    }

    if ($remoteUrl -match 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+?)(?:\.git)?$') {
        return "https://github.com/$($Matches.owner)/$($Matches.repo)/releases/latest/download"
    }

    return $null
}

$projectRoot = (Resolve-Path "$PSScriptRoot\..").Path
$artifactsRoot = Join-Path $projectRoot "artifacts"
$publishRoot = Join-Path $artifactsRoot "publish\$RuntimeIdentifier"
$appProject = Join-Path $projectRoot "src\Tourenplaner.CSharp.App\Tourenplaner.CSharp.App.csproj"
$launcherProject = Join-Path $projectRoot "src\Tourenplaner.CSharp.Launcher\Tourenplaner.CSharp.Launcher.csproj"
$appPublishRoot = Join-Path $artifactsRoot "obj\publish-app"
$launcherPublishRoot = Join-Path $artifactsRoot "obj\publish-launcher"
$resolvedReleaseBaseUrl = if ($ReleaseBaseUrl) { $ReleaseBaseUrl.TrimEnd('/') } else { Get-GitHubReleaseBaseUrl -RepositoryRoot $projectRoot }

New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
New-Item -ItemType Directory -Force -Path $appPublishRoot | Out-Null
New-Item -ItemType Directory -Force -Path $launcherPublishRoot | Out-Null

Write-Host "Publiziere App..."
dotnet publish $appProject `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -o $appPublishRoot

Write-Host "Publiziere Launcher..."
dotnet publish $launcherProject `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -o $launcherPublishRoot

Write-Host "Bereite Release-Ordner vor..."
Remove-Item -Recurse -Force $publishRoot
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishRoot "app") | Out-Null

Copy-Item -LiteralPath (Join-Path $launcherPublishRoot "GAWELA.Tourenplaner.exe") -Destination $publishRoot
Copy-Item -Path (Join-Path $appPublishRoot "*") -Destination (Join-Path $publishRoot "app") -Recurse -Force

if ($resolvedReleaseBaseUrl) {
    @{
        manifestUrl = "$resolvedReleaseBaseUrl/update-manifest.json"
    } | ConvertTo-Json | Set-Content -Path (Join-Path $publishRoot "update-config.json") -Encoding UTF8
}
else {
    Write-Warning "Es konnte keine Release-URL ermittelt werden. update-config.json wird nicht erzeugt."
}

Write-Host ""
Write-Host "Fertig. Startdatei:"
Write-Host "  $((Join-Path $publishRoot 'GAWELA.Tourenplaner.exe'))"

if ($BuildInstaller) {
    & (Join-Path $PSScriptRoot "build-installer.ps1") `
        -Configuration $Configuration `
        -RuntimeIdentifier $RuntimeIdentifier `
        -PublishedFilesRoot $publishRoot `
        -ReleaseBaseUrl $resolvedReleaseBaseUrl
}
