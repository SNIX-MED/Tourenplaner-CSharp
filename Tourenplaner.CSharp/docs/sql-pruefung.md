# PostgreSQL-Prüfung, 15.10.2026

## Konkreter Befund

Auftrag 221972 wurde ausschließlich lesend in der konfigurierten Datenbank
geprüft. Alle vier Produkte hatten dort bereits den Status `An Lager`.
Auch die Historie enthielt den Wechsel von `Auf dem Weg` zu `An Lager`.
Ein früherer fehlgeschlagener Versuch oder ein abweichender Datenbestand auf
einem anderen PC kann damit nicht ausgeschlossen werden.

## Korrekturen

- Produktstatus, Auftragsstatus, Produktbearbeitung und manuelles Gewicht in
  der Karte speichern einen getrennten Änderungskandidaten gezielt. Andere
  veraltete Aufträge können diese Einzeländerung nicht mehr zurückrollen.
- Der aktuelle Auftrag wird erst nach erfolgreicher Speicherung ersetzt.
  Hintergrundspeicherungen der Statusfelder werden serialisiert; Fehler
  werden angezeigt und der Datenbankstand wird wieder geladen.
- Avisierungsänderungen speichern nur betroffene Touren und den Auftrag
  transaktional im PostgreSQL-Modus.
- Automatische Auftragswartung speichert nur tatsächlich geänderte Aufträge.
- Die Sperrkennung bleibt vollständig innerhalb der 63-Byte-Grenze von
  PostgreSQL application_name, auch mit langen Namen und Umlauten.
- LISTEN wird vor der Meldung „verbunden“ aktiviert. Nach Verbindungsaufbau
  und Wiederverbindung wird ein vollständiges Nachladen angefordert.
  Ein periodischer Verbindungstest erkennt unterbrochene Verbindungen.
- Beide Einstellungsinterfaces verwenden denselben versionsgeprüften
  Speicherweg statt eines unbedingten Überschreibens.
- Unvollständige SQL-Konfiguration führt nicht still zu lokalem Dateibetrieb.

## Nachweise und Grenzen

Der gesamte Testlauf wurde mit der konfigurierten PostgreSQL-Verbindung
ausgeführt; Integrationstests verwendeten getrennte zufällig benannte
history_test_-Schemas und entfernten diese anschließend.
Geprüft wurden Commit/Rollback, veraltete Schreibversuche, eigene und fremde
Sperren, INSERT/UPDATE/DELETE-Trigger, Undo/Redo, Einstellungen/Fahrzeuge sowie
Produktstatus-Speicherung mit Benachrichtigung einer zweiten SQL-Verbindung
und erneutem Laden über neue Repository-Instanzen.

Die zweite Verbindung simuliert den Datenbankzugriff eines anderen PCs.
Eine manuelle Prüfung zweier laufender WPF-Anwendungen und der tatsächlichen
Verbindungsparameter auf dem anderen PC wurde nicht durchgeführt. Das
Wiederverbinden nach einem realen Netzwerkausfall wurde nicht künstlich am
Produktivserver erzwungen.

Weitere Prüfpunkte: Mitarbeiter- und Kalender-Repositories besitzen noch
keine durchgehende Versionsprüfung; ihre Sammelspeicherungen sind nicht
durchgehend atomar. Einige übrige UI-Aktionen ändern Modelle vor dem Speichern.
Diese sind von den hier abgesicherten Statusänderungen zu unterscheiden.
Deshalb ist dies kein Nachweis, dass jeder SQL- und UI-Pfad fehlerfrei ist.

Es wurden keine Produktiv-Auftragsdaten geändert und kein Release veröffentlicht.
