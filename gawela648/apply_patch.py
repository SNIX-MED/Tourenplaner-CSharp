from pathlib import Path
import sys

root = Path(sys.argv[1])

# Admin view: a shared image set represents a cabinet height class.
view = root / 'Views' / 'GawelaColorAdmin' / 'Configure.cshtml'
s = view.read_text(encoding='utf-8')
replacements = {
    'Produktgruppe – gemeinsame Bilder und Masken': 'Bildgruppe / Höhenvorlage – gemeinsame Bilder und Masken',
    'Verwenden mehrere Artikel exakt dieselbe Bildgeometrie und dieselben Masken, können Sie sie hier zu einer Gruppe zusammenfassen. Das aktuell geladene Produkt ist das <strong>Leitprodukt</strong>. Basisbild, Masken, Ebenen sowie Basis-/Fallback-RAL werden nur beim Leitprodukt gespeichert und von allen Gruppenmitgliedern gemeinsam verwendet.':
        'Verwenden mehrere Schränke dieselbe Bildvorlage für eine gemeinsame <strong>Höhenklasse</strong>, können Sie sie hier zusammenfassen. <strong>Breite und Tiefe dürfen ausdrücklich unterschiedlich sein.</strong> Das aktuell geladene Produkt ist das Leitprodukt. Basisbild, Masken, Ebenen sowie Basis-/Fallback-RAL werden nur beim Leitprodukt gespeichert und von allen Gruppenmitgliedern gemeinsam verwendet.',
    '<div class="alert alert-warning py-2"><strong>Wichtig:</strong> Nur Produkte gruppieren, deren Konfiguratorbild pixelgenau dieselbe Geometrie/Perspektive besitzt. Die Zielprodukte müssen dieselben Farbattribute besitzen; unterschiedliche produktbezogene Attribut-IDs sind erlaubt.</div>':
        '<div class="alert alert-info py-2"><strong>Prinzip der Höhenvorlage:</strong> Gruppieren Sie Produkte, für die bewusst dasselbe Konfiguratorbild derselben Schrankhöhe verwendet werden soll. Unterschiedliche Breiten und Tiefen sind zulässig und werden nicht als Ausschlusskriterium geprüft. Die Zielprodukte müssen lediglich die benötigten Farbattribute besitzen; unterschiedliche produktbezogene Attribut-IDs sind erlaubt.</div>',
    '<div class="form-group"><label>Gruppenname</label><input name="groupName" value="@Model.GroupName" class="form-control" style="max-width:640px" placeholder="z.B. Garderobenschrank 2-türig – gemeinsame Maske"/></div>':
        '<div class="form-group"><label>Höhenvorlage / Gruppenname</label><input name="groupName" value="@Model.GroupName" class="form-control" style="max-width:640px" placeholder="z.B. Schränke Höhe 1800 mm"/><small class="text-muted">Die Bezeichnung dient zur Organisation. Breite und Tiefe der Gruppenmitglieder dürfen voneinander abweichen.</small></div>',
    '<strong>Gemeinsame Produktgruppe:</strong> Dieses Produkt verwendet die Konfiguration „@Model.GroupName“ des Leitprodukts <strong>@Model.GroupMasterSku</strong> (Product-ID @Model.GroupMasterProductId). Basisbild, Masken, Ebenen und RAL-Vorgaben werden zentral dort gepflegt.':
        '<strong>Gemeinsame Höhenvorlage:</strong> Dieses Produkt verwendet die Bild- und Maskenkonfiguration „@Model.GroupName“ des Leitprodukts <strong>@Model.GroupMasterSku</strong> (Product-ID @Model.GroupMasterProductId). Breite und Tiefe dürfen vom dargestellten Leitprodukt abweichen; Basisbild, Masken, Ebenen und RAL-Vorgaben werden zentral beim Leitprodukt gepflegt.',
    'Leitprodukt / Gruppe bearbeiten': 'Leitprodukt / Höhenvorlage bearbeiten',
    'Produktgruppe speichern': 'Höhenvorlage speichern',
    'Produktgruppe aufheben': 'Höhenvorlage aufheben'
}
for old, new in replacements.items():
    if old in s:
        s = s.replace(old, new)
view.write_text(s, encoding='utf-8')

# Admin controller: wording only; storage format remains backwards-compatible.
controller = root / 'Controllers' / 'GawelaColorAdminController.cs'
s = controller.read_text(encoding='utf-8')
s = s.replace('Leitprodukt der Produktgruppe wurde nicht gefunden.', 'Leitprodukt der Höhenvorlage wurde nicht gefunden.')
s = s.replace('Das gewählte Leitprodukt gehört bereits zur Produktgruppe', 'Das gewählte Leitprodukt gehört bereits zur Höhenvorlage')
s = s.replace('gehört bereits zur Produktgruppe', 'gehört bereits zur Höhenvorlage')
s = s.replace('Produktgruppe gespeichert:', 'Höhenvorlage gespeichert:')
s = s.replace('Produkt(e) verwenden nun gemeinsam Basisbild, Masken, Ebenen und RAL-Vorgaben des Leitprodukts', 'Produkt(e) verwenden nun gemeinsam die Bild- und Maskenvorlage der Höhenklasse des Leitprodukts')
s = s.replace('Produktgruppe wurde aufgehoben. Die Konfiguration des bisherigen Leitprodukts bleibt erhalten.', 'Höhenvorlage wurde aufgehoben. Die Konfiguration des bisherigen Leitprodukts bleibt erhalten.')
controller.write_text(s, encoding='utf-8')

# Frontend disclaimer: the shared image is intentionally representative for height/color,
# while width/depth and construction details may differ from the sold product.
js = root / 'wwwroot' / 'gawela-color.js'
s = js.read_text(encoding='utf-8')
old = 'Bildschirmdarstellung unverbindlich. Farbabweichungen sind möglich. Die tatsächliche Ausführung muss nicht zwingend der dargestellten Abbildung entsprechen.'
new = 'Bildschirmdarstellung unverbindlich. Farbabweichungen sind möglich. Die Abbildung dient der Farb- und Höhenvisualisierung; Breite, Tiefe, Proportionen, Details und die tatsächliche Ausführung können vom dargestellten Bild abweichen.'
if old not in s:
    raise SystemExit('Expected 6.4.4 disclaimer text not found')
s = s.replace(old, new, 1)
js.write_text(s, encoding='utf-8')

# Version bump only. Existing product-groups.json remains fully compatible.
module = root / 'module.json'
s = module.read_text(encoding='utf-8')
s = s.replace('"Version": "6.4.7"', '"Version": "6.4.8"')
module.write_text(s, encoding='utf-8')
