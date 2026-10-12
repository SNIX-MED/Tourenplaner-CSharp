# Benutzertest: Teillieferungen

## Vorbereitung

1. Eine Datensicherung erstellen.
2. Einen Testauftrag mit gültiger Lieferadresse und zwei Produkten anlegen:
   - Produkt A: 10 Stück, 2 kg pro Stück
   - Produkt B: 4 Stück, 5 kg pro Stück
3. Sicherstellen, dass der Auftrag auf der Karte sichtbar und noch keiner Tour zugeordnet ist.

## Test 1: Erste Teillieferung erstellen

1. Bereich **Aufträge** öffnen.
2. Testauftrag mit der rechten Maustaste anklicken.
3. **Teillieferung erstellen** wählen.
4. Für Produkt A die Menge 6 und für Produkt B die Menge 0 eintragen.
5. Lieferdatum und eine Notiz eintragen.
6. **Lieferung erstellen** wählen.

Erwartet:

- Der Auftrag bleibt genau einmal als Auftrag gespeichert.
- In der Auftragsliste steht `0 von 14 geliefert · 1 Lieferung(en)`.
- Beim erneuten Öffnen des Dialogs beträgt die Restmenge von Produkt A 4 und von Produkt B 4.

## Test 2: Zweite Teillieferung erstellen

1. Erneut **Teillieferung erstellen** öffnen.
2. Produkt A: 4, Produkt B: 2 eintragen.
3. Lieferung speichern.

Erwartet:

- Zwei Lieferungen sind vorhanden.
- Restmenge Produkt A: 0.
- Restmenge Produkt B: 2.
- Auf der Karte erscheinen zwei getrennte Einträge mit `Teillieferung 1` und `Teillieferung 2`.

## Test 3: Mengenvalidierung

1. Eine weitere Teillieferung öffnen.
2. Bei Produkt B eine Menge grösser als 2 eingeben.
3. Speichern versuchen.

Erwartet:

- Das Speichern wird verhindert.
- Eine verständliche Meldung weist auf die verfügbare Restmenge hin.

## Test 4: Teillieferungen auf verschiedene Touren planen

1. Bereich **Karte** öffnen.
2. Den Eintrag `Teillieferung 1` zur Route hinzufügen.
3. Eine neue Tour speichern, beispielsweise `Test Teillieferung 1`.
4. Eine zweite neue Route beginnen.
5. Den Eintrag `Teillieferung 2` hinzufügen und als zweite Tour speichern.

Erwartet:

- Beide Touren existieren gleichzeitig.
- Tour 1 enthält nur Lieferung 1 und deren Mengen.
- Tour 2 enthält nur Lieferung 2 und deren Mengen.
- Das Planen von Lieferung 2 entfernt Lieferung 1 nicht aus Tour 1.
- Das berechnete Gewicht entspricht den Teilmengen.

## Test 5: Tour verschieben und entfernen

1. Eine der Teillieferungen in der Tourenansicht auf eine andere Tour verschieben.
2. Prüfen, dass nur diese Lieferung verschoben wurde.
3. Die Lieferung anschließend aus der Tour entfernen.

Erwartet:

- Die andere Teillieferung bleibt unverändert.
- Die entfernte Lieferung erscheint wieder als offen/planungsbereit auf der Karte.
- Ihre Mengen werden nicht gelöscht und nicht zur anderen Lieferung addiert.

## Test 6: Tour löschen

1. Eine Tour mit einer Teillieferung löschen.

Erwartet:

- Nur die auf dieser Tour enthaltene Lieferung wird wieder planungsbereit.
- Andere Touren und andere Teillieferungen desselben Auftrags bleiben erhalten.

## Test 7: Auftrag nachträglich bearbeiten

1. Auftrag bearbeiten.
2. Menge von Produkt A unter die bereits zugeteilten 10 Stück reduzieren.
3. Speichern versuchen.

Erwartet:

- Das Speichern wird verhindert.
- Die Meldung nennt die bereits zugeteilte Menge.
- Erhöhen der Auftragsmenge ist weiterhin möglich.

## Test 8: Neustart und Persistenz

1. Anwendung vollständig schließen und neu starten.
2. Auftrag und beide Touren erneut öffnen.

Erwartet:

- Lieferungen, Mengen, Nummern und Tourzuordnungen sind unverändert vorhanden.
- Eine weitere Teillieferung kann nur noch über tatsächlich offene Restmengen angelegt werden.

## Test 9: Bestehende Altdaten

1. Eine ältere Datenbasis mit einem bereits vollständig einer Tour zugeordneten Auftrag öffnen.
2. Auftrag und Tour kontrollieren.

Erwartet:

- Die bisherige Tourzuordnung bleibt bestehen.
- Intern wird automatisch eine vollständige Legacy-Lieferung erzeugt.
- Der bestehende Tourstopp bleibt sichtbar und bearbeitbar.

## Abnahmekriterien

Die Funktion gilt als bestanden, wenn:

- keine Summe der Teillieferungen die Auftragsmenge überschreiten kann;
- mehrere Lieferungen desselben Auftrags auf verschiedenen Touren bestehen bleiben;
- Entfernen, Verschieben und Löschen immer nur die betroffene Lieferung verändern;
- Neustart, JSON-Speicherung und PostgreSQL-Speicherung dieselben Daten erhalten;
- alte Aufträge und Touren ohne manuelle Nachbearbeitung geöffnet werden können.
