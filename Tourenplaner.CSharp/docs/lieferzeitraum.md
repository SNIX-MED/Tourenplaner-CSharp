# Lieferzeitraum auf der Startseite

Das Textfeld **Lieferzeitraum** wird manuell oder über das XML-Feld
`Lieferdatum2` / `lieferdatum2` befüllt. Die Startseite berechnet daraus beim
Laden einen Zeitraum und sortiert ihn gemeinsam mit den festen Lieferdaten
unter **Aufträge mit Fixtermin** ein. Der Originaltext bleibt sichtbar; es
wird dadurch kein Lieferdatum gesetzt und keine Tour umgeplant.

## Erkannte Schreibweisen

| Beispiel | Interpretation |
| --- | --- |
| `31.10.2026`, `31/10/2026`, `2026-10-31` | Einzelner Tag |
| `19.–23.10.2026`, `19-23.10.2026` | 19. bis einschließlich 23. Oktober |
| `31.10 bis 5.11.2026` | 31. Oktober bis einschließlich 5. November |
| `von 19.10.2026 bis 23.10.2026` | Datumsbereich |
| `zwischen 19.10.2026 und 23.10.2026` | Datumsbereich |
| `Ab 31.10.2026` | Ab diesem Tag, ohne festes Enddatum |
| `bis 31.10.2026` | Ohne festen Beginn, bis einschließlich dieses Tags |
| `KW51`, `Kalenderwoche 51`, `KW 51/2026` | Montag bis Sonntag der ISO-Kalenderwoche |
| `ab KW51` | Ab Montag dieser Kalenderwoche, ohne Enddatum |
| `bis KW51` | Bis einschließlich Sonntag dieser Kalenderwoche |
| `KW3 bis Kw6`, `KW 3–6`, `KW03/2027 bis KW06/2027` | Montag der ersten bis Sonntag der letzten KW |
| `KW51 bis KW3` | Bereich über den Jahreswechsel |
| `Oktober 2026` | Ganzer Monat |
| `Anfang Oktober` | 1. bis 10. Oktober |
| `Mitte Oktober` | 11. bis 20. Oktober |
| `Ende Oktober` | 21. Oktober bis Monatsende |
| `Anfang bis Mitte Oktober` | 1. bis 20. Oktober |
| `ab Ende Oktober` | Ab 21. Oktober, ohne Enddatum |
| `Dezember bis Januar` | Bereich über den Jahreswechsel |

Groß-/Kleinschreibung, zusätzliche Leerzeichen und verschiedene Bindestriche
sind zulässig. Monatsnamen können auch abgekürzt werden, zum Beispiel `Okt.`.
Ein vorangestelltes `ca.`, `circa` oder `voraussichtlich` ist zulässig;
der Originaltext zeigt weiterhin, dass die Angabe ungefähr ist.

## Jahresangaben

- Ohne Jahr gilt das aktuelle Kalenderjahr, bei KW das aktuelle ISO-Wochenjahr.
  Dadurch gehört etwa am 1. Januar 2027 eine jahreslose KW noch zum ISO-Jahr
  2026. Vergangene Angaben werden nicht automatisch ins nächste Jahr verschoben.
- Zweistellige Jahre werden als 2000–2099 gelesen: `31.10.26` entspricht 2026.
- Ein nur am Ende angegebenes Jahr gilt für den gesamten Bereich.
- Bei einem Monats-/KW-Wechsel von Dezember/hoher KW zu Januar/niedriger KW
  wird das fehlende Jahr passend ergänzt. Sind beide Jahre ausdrücklich
  angegeben, werden sie unverändert verwendet; rückwärts laufende Bereiche
  sind ungültig.
- Für zukünftige Jahre sollte das Jahr ausdrücklich eingegeben werden,
  beispielsweise `KW3 bis KW6 2027`. Jahreslose Angaben werden bei einem
  Jahreswechsel neu anhand des dann aktuellen Jahres interpretiert.

## Anzeige und Sortierung

- Ein gültiges konkretes **Lieferdatum** hat Vorrang vor dem Textzeitraum.
  Die bisherige Regel bleibt erhalten: feste Daten mit „Lieferung kann früher
  erfolgen“ erscheinen nicht als Fixtermin; es erfolgt dann auch kein
  Ausweichen auf den Textzeitraum.
- Ohne konkretes Lieferdatum zählt der erkannte Textzeitraum.
- Sortiert wird nach Beginn; bei einem offenen Beginn (`bis …`) nach Ende.
  Bei gleichen Daten folgen Kunde und Auftragsnummer als stabile Sortierung.
- Ein laufender Zeitraum bleibt bis einschließlich seines letzten Tags sichtbar.
  `Ab …` bleibt mangels Enddatum sichtbar, solange der Auftrag nicht archiviert
  ist. Zeiträume mit zukünftigem Beginn werden bereits vorher angezeigt.
- Abgelaufene Zeiträume, archivierte Aufträge sowie ungültige oder unklare
  Angaben wie `nach Absprache`, `KW3 oder KW6` und `31.02.2026` erscheinen nicht.
  Andere Freitexte werden nicht nach zufällig enthaltenen Datumsfragmenten durchsucht.
- Die Startseite zeigt den berechneten Bereich und darunter den Originaltext.

Die Erkennung ist bewusst regelbasiert. Nicht unterstützt sind relative
Versprechen wie „in drei Wochen“ oder beliebige freie Sätze. Änderungen an
Import oder Auftragsbearbeitung stehen nach dem nächsten Aktualisieren der
Startseite in der Liste zur Verfügung.
