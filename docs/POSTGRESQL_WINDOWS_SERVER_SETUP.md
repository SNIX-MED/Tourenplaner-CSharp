# PostgreSQL auf lokalem Windows-Server fuer den Tourenplaner

Diese Variante ist kostenlos und fuer euren Mehrbenutzerbetrieb geeignet.

## Ziel

- PostgreSQL auf einem lokalen Windows-Server betreiben
- Mehrere Geraete im Netzwerk greifen auf dieselbe Datenbank zu
- Der Tourenplaner nutzt danach `PostgreSQL Mehrbenutzer`

## Voraussetzungen

- Windows-Server mit Administratorrechten
- Netzwerkverbindung von den Clients zum Server
- Freier TCP-Port `5432` oder ein eigener Port
- Ein Passwort fuer den PostgreSQL-Superuser `postgres`
- Ein Passwort fuer den Tourenplaner-App-Benutzer

## Kostenloser PostgreSQL

Verwendet wird die freie Community-Version von PostgreSQL fuer Windows.

- Offizieller Download: https://www.postgresql.org/download/windows/
- Der Windows-Installer kann laut offizieller Doku auch unbeaufsichtigt ausgefuehrt werden.

## Empfohlener Ablauf

### Bereits vorhandene Installation unter `C:\Tourenplaner\data`

Wenn PostgreSQL auf dem Server bereits installiert ist und das Datenverzeichnis unter
`C:\Tourenplaner\data` liegt, dann kannst du das Skript ohne Neuinstallation für die
Konfiguration weiterverwenden.

### 1. Installer auf den Server laden

Lade den kostenlosen Windows-Installer auf den Server, zum Beispiel nach:

`C:\Install\PostgreSQL\postgresql-windows-x64.exe`

### 2. Setup-Skript auf den Server kopieren

Kopiere dieses Skript auf den Server:

`scripts\setup-postgresql-windows-server.ps1`

### 3. PowerShell als Administrator starten

### 4. Skript ausfuehren

Beispiel:

```powershell
Set-ExecutionPolicy -Scope Process Bypass

.\setup-postgresql-windows-server.ps1 `
  -InstallerPath "C:\Install\PostgreSQL\postgresql-windows-x64.exe" `
  -SuperUserPassword "DEIN_POSTGRES_PASSWORT" `
  -AppPassword "DEIN_APP_PASSWORT" `
  -AppDatabase "tourenplaner" `
  -AppSchema "app" `
  -AppUser "tourenplaner_app" `
  -ClientSubnet "192.168.1.0/24"
```

### Beispiel fuer bestehende Installation in `C:\Tourenplaner\data`

```powershell
Set-ExecutionPolicy -Scope Process Bypass

.\setup-postgresql-windows-server.ps1 `
  -PreferExistingDataDirectory `
  -DataDirectory "C:\Tourenplaner\data" `
  -SuperUserPassword "DEIN_POSTGRES_PASSWORT" `
  -AppPassword "DEIN_APP_PASSWORT" `
  -AppDatabase "tourenplaner" `
  -AppSchema "app" `
  -AppUser "tourenplaner_app" `
  -ClientSubnet "192.168.1.0/24"
```

In diesem Modus macht das Skript:

- keine Neuinstallation
- Nutzung des vorhandenen Datenverzeichnisses
- Anpassung von `postgresql.conf`
- Anpassung von `pg_hba.conf`
- Firewall-Freigabe fuer PostgreSQL
- Anlegen von Datenbank, Schema und App-Benutzer

Das Skript erledigt:

- Installation von PostgreSQL, falls noch nicht vorhanden
- Aktivierung von Netzwerkzugriff
- Anpassung von `postgresql.conf`
- Anpassung von `pg_hba.conf`
- Firewall-Freigabe fuer den PostgreSQL-Port
- Anlegen von Datenbank, Schema und App-Benutzer

## Werte danach im Tourenplaner eintragen

In `Einstellungen > Allgemein > Datenspeicher`:

- Speicherart: `PostgreSQL Mehrbenutzer`
- Host: Servername oder feste Server-IP
- Port: `5432`
- Datenbank: `tourenplaner`
- Schema: `app`
- Benutzername: `tourenplaner_app`
- Passwort: das beim Skript verwendete App-Passwort

Danach:

1. `PostgreSQL-Verbindung testen`
2. `PostgreSQL aktivieren und neu starten`

## Empfehlung fuer den Host

Am besten in der App nicht den wechselnden Client-Namen verwenden, sondern:

- den festen Servernamen im Netzwerk, oder
- eine feste interne IP des Servers

## Sicherheit

- Fuer den App-Benutzer ein eigenes, starkes Passwort verwenden
- Zugriff im Skript ueber `ClientSubnet` auf euer internes Netz begrenzen
- PostgreSQL nicht unnoetig ins Internet freigeben

## Was ich noch nicht direkt remote gemacht habe

Ich habe das Setup hier vorbereitet, aber nicht direkt auf deinem Server ausgefuehrt, weil ich keinen Remote-Zugriff auf den Windows-Server in dieser Sitzung habe.

Wenn du mir Servername und einen moeglichen Zugriffsweg gibst, kann ich dir als naechsten Schritt auch ein Remote-Ausfuehrungsskript fuer PowerShell Remoting vorbereiten.
