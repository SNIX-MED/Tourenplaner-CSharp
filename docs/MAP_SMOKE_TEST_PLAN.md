# Map Smoke Test Plan

Stand: 10.04.2026

## Ziel
- Schnelle manuelle Verifikation der Kartenansicht nach Refactoring:
  - ausgelagerte HTML/JS-Map-Dokumenterzeugung
  - zentraler Map-Refresh-Scheduler mit getrennten Debounce-Profilen

## Vorbedingungen
- App startet lokal ohne Build-Fehler.
- Es existieren Auftraege mit gueltigen Koordinaten.
- Mindestens eine Tour mit RouteStopps ist vorhanden (oder kann erzeugt werden).

## Klickpfad 1: Initiales Laden der Karte
1. App starten.
2. In den Bereich `Karte` wechseln.
3. 3-5 Sekunden warten, ohne weitere Interaktion.

Erwartung:
- Karte wird angezeigt (kein dauerhafter Fallback-Hinweis).
- Marker sind sichtbar.
- Kein Flackern bei erstem Laden.

## Klickpfad 2: Selektion Orders -> Marker Highlight
1. In der Kartenansicht nacheinander 5 verschiedene Orders in der Liste anklicken.
2. Danach schnell zwischen 2 Orders hin- und herwechseln.

Erwartung:
- Marker-Highlight folgt der Auswahl.
- Selektion reagiert direkt (keine wahrnehmbare Verzoegerung).
- Kein "Nachziehen" alter Selektionen.

## Klickpfad 3: Detailfilter/Status aendern (datenintensiv)
1. In der Kartenansicht `DetailSelectedStatus` bzw. `DetailOrderStatus` aendern.
2. Direkt danach auch `DetailSelectedAvisoStatus` bzw. `DetailAvisoStatus` aendern.
3. Optional die Schritte 1-2 schnell hintereinander wiederholen.

Erwartung:
- Marker und Route aktualisieren sich konsistent.
- Bei schnellen Folgeaenderungen keine Update-Staus oder UI-Ruckler.
- Endzustand entspricht der zuletzt gesetzten Filterkombination.

## Klickpfad 4: InfoCards Sichtbarkeit und Skalierung
1. Sichtbarkeit der Pin-InfoCards ein-/ausschalten.
2. Unmittelbar danach den Scale-Wert der Pin-InfoCards mehrfach aendern.
3. Mit offenem Marker-Popup wiederholen.

Erwartung:
- Sichtbarkeit und Groesse werden zeitnah uebernommen.
- Keine veralteten Popup-Zustaende nach schneller Folgeinteraktion.
- Keine unerwarteten Spruenge der Karte.

## Klickpfad 5: Route-Aenderung + Drag and Drop
1. Einen RouteStopp in der Route-Liste per Drag-and-Drop verschieben.
2. Danach einen weiteren Stopp auf andere Position ziehen.
3. Anschliessend auf einen RouteStopp klicken und die Selektion pruefen.

Erwartung:
- Reihenfolge wird korrekt aktualisiert.
- Route in der Karte passt zur neuen Reihenfolge.
- Selektierter RouteStopp wird korrekt hervorgehoben.

## Abnahmekriterium
- Alle 5 Klickpfade ohne Fehlerdialog, ohne eingefrorene UI und ohne inkonsistenten Endzustand.
