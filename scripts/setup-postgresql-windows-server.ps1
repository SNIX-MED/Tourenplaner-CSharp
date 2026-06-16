param(
    [string]$InstallerPath = "",
    [string]$InstallRoot = "C:\Program Files\PostgreSQL\17",
    [string]$DataDirectory = "C:\Tourenplaner\data",
    [switch]$PreferExistingDataDirectory,
    [string]$ServiceName = "postgresql-x64-17",
    [string]$SuperUserName = "postgres",
    [Parameter(Mandatory = $true)]
    [string]$SuperUserPassword,
    [string]$AppDatabase = "tourenplaner",
    [string]$AppSchema = "app",
    [string]$AppUser = "tourenplaner_app",
    [Parameter(Mandatory = $true)]
    [string]$AppPassword,
    [int]$Port = 5432,
    [string]$ClientSubnet = "192.168.0.0/24",
    [switch]$UseSsl,
    [switch]$SkipFirewallRule
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Administrator {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Bitte PowerShell als Administrator starten."
    }
}

function Get-PostgreSqlPsqlPath {
    $command = Get-Command "psql.exe" -ErrorAction SilentlyContinue
    if ($command -and $command.Source -and (Test-Path $command.Source)) {
        return [string]$command.Source
    }

    $candidates = Get-ChildItem -Path "C:\Program Files\PostgreSQL" -Filter "psql.exe" -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending
    if ($candidates.Count -gt 0) {
        return [string]$candidates[0].FullName
    }

    return $null
}

function Install-PostgreSqlIfNeeded {
    param([string]$CurrentPsqlPath)

    if ($CurrentPsqlPath) {
        return $CurrentPsqlPath
    }

    if ($PreferExistingDataDirectory) {
        throw "psql.exe wurde nicht gefunden. Bei einer bestehenden Installation bitte zuerst sicherstellen, dass die PostgreSQL-Commandlinetools installiert sind oder psql.exe im PATH bzw. unter C:\Program Files\PostgreSQL verfuegbar ist."
    }

    if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
        throw "PostgreSQL ist noch nicht installiert. Bitte den kostenlosen Windows-Installer von https://www.postgresql.org/download/windows/ herunterladen und den Pfad per -InstallerPath uebergeben."
    }

    if (-not (Test-Path -LiteralPath $InstallerPath)) {
        throw "Installer nicht gefunden: $InstallerPath"
    }

    Write-Step "Installiere PostgreSQL unattended"
    $arguments = @(
        "--mode", "unattended",
        "--unattendedmodeui", "minimal",
        "--create_shortcuts", "0",
        "--enable-components", "server,commandlinetools",
        "--disable-components", "pgAdmin,stackbuilder",
        "--prefix", "`"$InstallRoot`"",
        "--datadir", "`"$DataDirectory`"",
        "--serverport", $Port,
        "--servicename", $ServiceName,
        "--superaccount", $SuperUserName,
        "--superpassword", $SuperUserPassword,
        "--servicepassword", $SuperUserPassword
    )

    $process = Start-Process -FilePath $InstallerPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "Die PostgreSQL-Installation wurde mit ExitCode $($process.ExitCode) beendet."
    }

    $resolvedPsql = Get-PostgreSqlPsqlPath
    if (-not $resolvedPsql) {
        throw "PostgreSQL wurde installiert, aber psql.exe konnte nicht gefunden werden."
    }

    return $resolvedPsql
}

function Get-PostgreSqlService {
    $services = Get-Service | Where-Object { $_.Name -like "postgresql*" } | Sort-Object Name
    if ($services.Count -eq 0) {
        throw "Kein PostgreSQL-Dienst gefunden."
    }

    $exact = $services | Where-Object { $_.Name -eq $ServiceName } | Select-Object -First 1
    if ($exact) {
        return $exact
    }

    return $services[0]
}

function Get-ServiceImagePath {
    param([string]$ResolvedServiceName)
    return (Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$ResolvedServiceName").ImagePath
}

function Get-DataDirectoryFromServiceImagePath {
    param([string]$ImagePath)
    $match = [regex]::Match($ImagePath, '-D\s+"([^"]+)"')
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    $fallbackMatch = [regex]::Match($ImagePath, '-D\s+([^\s]+)')
    if ($fallbackMatch.Success) {
        return $fallbackMatch.Groups[1].Value.Trim('"')
    }

    throw "Das PostgreSQL-Datenverzeichnis konnte aus dem Dienstpfad nicht ermittelt werden."
}

function Set-ConfigValue {
    param(
        [string]$Path,
        [string]$Key,
        [string]$Value
    )

    $content = Get-Content -LiteralPath $Path
    $pattern = "^\s*#?\s*$([regex]::Escape($Key))\s*="
    $replacement = "$Key = $Value"
    $updated = $false

    for ($index = 0; $index -lt $content.Count; $index++) {
        if ($content[$index] -match $pattern) {
            $content[$index] = $replacement
            $updated = $true
            break
        }
    }

    if (-not $updated) {
        $content += $replacement
    }

    Set-Content -LiteralPath $Path -Value $content -Encoding UTF8
}

function Ensure-HbaEntry {
    param(
        [string]$Path,
        [string]$Subnet
    )

    $entry = "host    all             all             $Subnet            scram-sha-256"
    $content = Get-Content -LiteralPath $Path
    if ($content -contains $entry) {
        return
    }

    Add-Content -LiteralPath $Path -Value ""
    Add-Content -LiteralPath $Path -Value "# Tourenplaner Netzwerkzugriff"
    Add-Content -LiteralPath $Path -Value $entry
}

function Ensure-FirewallRule {
    param([int]$ResolvedPort)

    $displayName = "PostgreSQL $ResolvedPort"
    $existingRule = Get-NetFirewallRule -DisplayName $displayName -ErrorAction SilentlyContinue
    if ($existingRule) {
        return
    }

    New-NetFirewallRule `
        -DisplayName $displayName `
        -Direction Inbound `
        -Action Allow `
        -Protocol TCP `
        -LocalPort $ResolvedPort | Out-Null
}

function Escape-SqlLiteral {
    param([string]$Value)
    if ($null -eq $Value) {
        return ""
    }

    return $Value.Replace("'", "''")
}

function Invoke-Psql {
    param(
        [string]$PsqlPath,
        [string]$Database,
        [string]$Sql
    )

    $escapedSql = $Sql.Replace('"', '\"')
    $escapedArgs = @(
        "--host", "127.0.0.1",
        "--port", "$Port",
        "--username", $SuperUserName,
        "--dbname", $Database,
        "--no-password",
        "--command", $escapedSql
    )

    $oldPassword = $env:PGPASSWORD
    try {
        $env:PGPASSWORD = $SuperUserPassword
        & $PsqlPath @escapedArgs
        if ($LASTEXITCODE -ne 0) {
            throw "psql-Befehl fehlgeschlagen."
        }
    }
    finally {
        $env:PGPASSWORD = $oldPassword
    }
}

Assert-Administrator

Write-Step "Pruefe PostgreSQL-Installation"
$psqlPath = Get-PostgreSqlPsqlPath
$psqlPath = Install-PostgreSqlIfNeeded -CurrentPsqlPath $psqlPath

Write-Step "Ermittle PostgreSQL-Dienst"
$service = Get-PostgreSqlService
$resolvedServiceName = $service.Name
$resolvedDataDirectory = $null

if ($PreferExistingDataDirectory -and (Test-Path -LiteralPath $DataDirectory)) {
    $resolvedDataDirectory = (Resolve-Path -LiteralPath $DataDirectory).Path
    Write-Step "Verwende vorhandenes Datenverzeichnis: $resolvedDataDirectory"
}
else {
    $serviceImagePath = Get-ServiceImagePath -ResolvedServiceName $resolvedServiceName
    $resolvedDataDirectory = Get-DataDirectoryFromServiceImagePath -ImagePath $serviceImagePath
}

$postgresqlConfPath = Join-Path $resolvedDataDirectory "postgresql.conf"
$pgHbaConfPath = Join-Path $resolvedDataDirectory "pg_hba.conf"

if (-not (Test-Path -LiteralPath $postgresqlConfPath)) {
    throw "postgresql.conf wurde nicht gefunden: $postgresqlConfPath"
}

if (-not (Test-Path -LiteralPath $pgHbaConfPath)) {
    throw "pg_hba.conf wurde nicht gefunden: $pgHbaConfPath"
}

Write-Step "Konfiguriere PostgreSQL fuer Netzwerkzugriff"
Set-ConfigValue -Path $postgresqlConfPath -Key "listen_addresses" -Value "'*'"
Set-ConfigValue -Path $postgresqlConfPath -Key "port" -Value "$Port"
Ensure-HbaEntry -Path $pgHbaConfPath -Subnet $ClientSubnet

if (-not $SkipFirewallRule) {
    Write-Step "Erstelle Firewall-Freigabe fuer Port $Port"
    Ensure-FirewallRule -ResolvedPort $Port
}

Write-Step "Starte PostgreSQL-Dienst neu"
Restart-Service -Name $resolvedServiceName -Force
Start-Sleep -Seconds 3

$appUserSql = Escape-SqlLiteral $AppUser
$appPasswordSql = Escape-SqlLiteral $AppPassword
$appDatabaseSql = Escape-SqlLiteral $AppDatabase
$appSchemaSql = Escape-SqlLiteral $AppSchema

Write-Step "Lege Datenbankrolle an"
Invoke-Psql -PsqlPath $psqlPath -Database "postgres" -Sql @"
DO \$\$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '$appUserSql') THEN
        EXECUTE 'CREATE ROLE "$appUserSql" LOGIN PASSWORD ''$appPasswordSql''';
    ELSE
        EXECUTE 'ALTER ROLE "$appUserSql" WITH LOGIN PASSWORD ''$appPasswordSql''';
    END IF;
END
\$\$;
"@

Write-Step "Lege Tourenplaner-Datenbank an"
Invoke-Psql -PsqlPath $psqlPath -Database "postgres" -Sql @"
DO \$\$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = '$appDatabaseSql') THEN
        EXECUTE 'CREATE DATABASE "$appDatabaseSql" OWNER "$appUserSql"';
    END IF;
END
\$\$;
"@

Write-Step "Lege Schema und Berechtigungen an"
Invoke-Psql -PsqlPath $psqlPath -Database $AppDatabase -Sql @"
CREATE SCHEMA IF NOT EXISTS "$appSchemaSql" AUTHORIZATION "$appUserSql";
ALTER SCHEMA "$appSchemaSql" OWNER TO "$appUserSql";
GRANT ALL ON SCHEMA "$appSchemaSql" TO "$appUserSql";
ALTER DEFAULT PRIVILEGES IN SCHEMA "$appSchemaSql" GRANT ALL ON TABLES TO "$appUserSql";
ALTER DEFAULT PRIVILEGES IN SCHEMA "$appSchemaSql" GRANT ALL ON SEQUENCES TO "$appUserSql";
"@

Write-Step "Server-Setup abgeschlossen"
Write-Host ""
Write-Host "Servername fuer die App: $env:COMPUTERNAME" -ForegroundColor Green
Write-Host "Port: $Port" -ForegroundColor Green
Write-Host "Datenbank: $AppDatabase" -ForegroundColor Green
Write-Host "Schema: $AppSchema" -ForegroundColor Green
Write-Host "App-Benutzer: $AppUser" -ForegroundColor Green
Write-Host ""
Write-Host "In der Tourenplaner-App eintragen:" -ForegroundColor Yellow
Write-Host "  Speicherart: PostgreSQL Mehrbenutzer"
Write-Host "  Host: $env:COMPUTERNAME"
Write-Host "  Port: $Port"
Write-Host "  Datenbank: $AppDatabase"
Write-Host "  Schema: $AppSchema"
Write-Host "  Benutzername: $AppUser"
Write-Host "  Passwort: <dein App-Passwort>"
