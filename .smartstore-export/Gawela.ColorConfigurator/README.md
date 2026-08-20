# GAWELA Farbkonfigurator für Smartstore 6.4

## Was dieses Modul macht

Das Modul stellt einen eigenen Smartstore-Page-Builder-Block **GAWELA Farbkonfigurator** bereit.

Es ist für die GAWELA-Metallschränke mit zwei voneinander unabhängigen Farbattributen ausgelegt:

- `Farben Korpus/Gestell ML`
- `Farben Türen/Schubladen ML`

Der bestehende Smartstore-Warenkorb und die bestehenden Produktattribute werden **nicht verändert**.
Der Block liest lediglich die aktuell ausgewählten RAL-Werte und erzeugt daraus im Browser eine dynamische Produktvorschau.

Pro Produkt werden nur benötigt:

1. `base.webp`
2. `mask-corpus.png`
3. `mask-doors.png`

Es müssen keine 256 Kombinationen als Bilder gespeichert werden.

---

## Technischer Stand

Erstellt für **Smartstore 6.4.0 / .NET 10**.

Der Quellcode orientiert sich an den offiziellen Smartstore-6.4.0-Modul- und Page-Builder-APIs:
`ModuleBase`, `IConfigurable`, `StarterBase`, `IBlock`, `BlockHandlerBase<T>` und den Block-Templates
unter `Views/Shared/BlockTemplates/<systemName>/`.

Das Paket ist als **Source-Modul zum direkten Einfügen in die Smartstore-Solution** gedacht.
Der endgültige Build erfolgt innerhalb Ihrer Smartstore-6.4-Solution.

---

## 1. Modul in das Smartstore-Projekt kopieren

Diesen kompletten Ordner nach

`<Smartstore>/src/Smartstore.Modules/Gawela.ColorConfigurator`

kopieren.

Danach in Visual Studio:

1. `Smartstore.sln` öffnen
2. Solution-Ordner **Modules**
3. Rechtsklick → **Add / Existing Project**
4. `Gawela.ColorConfigurator.csproj` auswählen
5. Solution bauen

Das Projekt schreibt seine Ausgabe automatisch nach:

`src/Smartstore.Web/Modules/Gawela.ColorConfigurator`

---

## 2. Modul installieren

Smartstore starten.

Im Backend:

`Plugins / Plugins verwalten`

bzw. Ihre Modulverwaltung öffnen.

**GAWELA Farbkonfigurator** suchen und installieren.

Danach **Konfigurieren** öffnen.

---

## 3. Ersten Testartikel hinterlegen

Testartikel:

`NC-10.SSBM 1903230403`

Im Modul unter **Konfigurieren**:

1. Artikelnummer eingeben
2. `base.webp` auswählen
3. `mask-corpus.png` auswählen
4. `mask-doors.png` auswählen
5. Speichern

Das Modul löst die Artikelnummer automatisch zur internen Smartstore-Product-ID auf.

Die Dateien werden gespeichert unter:

`Smartstore.Web/App_Data/GawelaColorAssets/<ProductId>/`

Sie liegen damit nicht öffentlich im Dateisystem.
Der öffentliche Zugriff erfolgt kontrolliert über den Modul-Controller.

---

## 4. Vorgaben für die Bilder

### base.webp

- normales Produktbild
- empfohlen: Korpus und Türen RAL 7035
- Dateiformat zwingend WebP

### mask-corpus.png

- exakt gleiche Pixelgrösse wie `base.webp`
- weisse/sichtbare Bereiche = einfärben
- schwarze/transparente Bereiche = unverändert lassen
- Korpus, Seitenwand, Deckel, Rahmen, Sockel markieren
- Türen und Schloss aussparen

### mask-doors.png

- exakt gleiche Pixelgrösse
- nur die beiden Türflächen markieren
- Schloss und Spalten aussparen
- PNG

Die Masken dürfen auch transparente Hintergründe besitzen.
Das JavaScript nutzt Helligkeit × Alpha als Maskenstärke.

---

## 5. Page Builder einrichten

Im Smartstore Page Builder eine Story erstellen.

Den Block

**GAWELA Farbkonfigurator**

einfügen.

Empfohlene Widget-Zone für den Test:

`productdetails_pictures_top`

Die Story zunächst nur beim Testprodukt einsetzen.

Später kann dieselbe Story auf alle relevanten Produktdetailseiten ausgedehnt werden:
Wenn für ein Produkt keine drei Dateien vorhanden sind, bleibt der Block unsichtbar.

---

## 6. Block-Einstellungen

Standardwerte:

- Korpus-Attribut: `Farben Korpus/Gestell ML`
- Tür-Attribut: `Farben Türen/Schubladen ML`
- Basis Korpus: `7035`
- Basis Türen: `7035`
- Fallback Korpus: `7035`
- Fallback Türen: `7035`
- Galerie ersetzen: **Nein**

Für den ersten Test **Galerie ersetzen = Nein** lassen.

Dann wird die dynamische Vorschau zusätzlich oberhalb der bestehenden Galerie angezeigt.

Wenn alles stimmt, kann **Galerie ersetzen** aktiviert werden.
Dann blendet das Modul `#pd-gallery-container` erst aus, nachdem alle drei Farbbilder erfolgreich geladen wurden.

---

## 7. Wie die Smartstore-Farben erkannt werden

Smartstore 6.4 rendert Produktvarianten als `.choice`-Blöcke mit `.choice-label`.

Das Modul sucht exakt nach den sichtbaren Bezeichnungen:

- `Farben Korpus/Gestell ML`
- `Farben Türen/Schubladen ML`

und liest die markierte Radio-/Box-/Dropdown-Auswahl.

Aus Beschriftung, Tooltip oder `aria-label` wird der RAL-Code extrahiert.

Beispiele:

- `RAL 7016 Anthrazitgrau` → `7016`
- `RAL 5010 Enzianblau` → `5010`

Die normale Smartstore-Auswahl selbst bleibt unverändert und wird weiterhin in den Warenkorb übernommen.

---

## 8. Dynamische Einfärbung

Die Vorschau wird per HTML Canvas im Browser erzeugt.

Der Farbalgorithmus:

1. liest das Originalpixel
2. übernimmt dessen Helligkeit
3. kombiniert diese Helligkeit mit der gewünschten RAL-sRGB-Näherung
4. mischt das Resultat entsprechend der Maskenstärke

Dadurch bleiben Schatten und leichte Lichtunterschiede sichtbar.

Wenn die gewählte Farbe der Basisfarbe entspricht (standardmässig RAL 7035),
bleibt der Originalpixel unverändert.

---

## 9. Farben

`wwwroot/colors.json`

enthält die 16 GAWELA-Standardfarben.

Enthalten sind:

- RAL
- Name
- RGB
- HEX
- Feld für NCS

**Wichtig:** RGB/HEX sind nur Bildschirm-Näherungen.
Vor Produktivbetrieb sollte die Tabelle gegen Ihre autoritative RAL-Farbreferenz geprüft werden.

Die NCS-Felder sind absichtlich leer.
Nur verifizierte NCS-Näherungen eintragen.

Beispiel:

```json
"7016": {
  "name": "Anthrazitgrau",
  "hex": "#383E42",
  "rgb": [56, 62, 66],
  "ncs": "VERIFIZIERTER NCS-WERT"
}
```

Sobald `ncs` befüllt ist, zeigt der Block automatisch:

`NCS ähnlich: ...`

---

## 10. Viele Produkte ausrollen

Nach der einmaligen Einrichtung des Page-Builder-Blocks müssen Sie für ein neues Produkt nur:

1. Modul-Konfiguration öffnen
2. Artikelnummer eingeben
3. Basisbild hochladen
4. Korpusmaske hochladen
5. Türmaske hochladen
6. speichern

Kein neuer Code.
Kein neuer Page-Builder-Block.
Keine 256 Produktbilder.

---

## 11. Datenablage

Die Uploads liegen in:

`App_Data/GawelaColorAssets/<ProductId>/`

Beispiel:

```text
App_Data/
└── GawelaColorAssets/
    └── 12345/
        ├── base.webp
        ├── mask-corpus.png
        └── mask-doors.png
```

Beim Deinstallieren des Moduls werden diese Produktdaten bewusst nicht automatisch gelöscht.

---

## 12. Fehlerverhalten

- keine Produkt-ID gefunden → Block bleibt unsichtbar
- Farbbilder nicht vollständig vorhanden → Block bleibt unsichtbar
- Bildgrössen stimmen nicht überein → Block bleibt aktiv, Fehler in Browser-Konsole
- Farbattribut nicht gefunden → Vorschau nutzt Fallback-RAL und zeigt eine Warnung
- RAL nicht in `colors.json` → Fallback-RAL

---

## 13. Empfohlener Test

Für den SSB 2010/4r:

- 7035 / 7035
- 7016 / 7035
- 7035 / 5010
- 7016 / 5010
- 9005 / 3020

Prüfen:

- Korpusfarbe verändert nur Korpus
- Türfarbe verändert nur Türen
- Schloss bleibt unverändert
- Türspalten bleiben unverändert
- Schatten bleiben sichtbar
- Smartstore-Warenkorb enthält weiterhin die korrekten Produktattribute
- Mobilansicht funktioniert
