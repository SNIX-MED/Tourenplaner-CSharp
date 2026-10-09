// Run with: node Tourenplaner.CSharp/tests/map-pin-popup.test.cjs
// Executes the production popup builder embedded in the map document.
function runMapPinPopupTests(source) {
  const start = source.indexOf('const escapeHtml =');
  const end = source.indexOf('const applyBaseStyle =', start);
  if (start < 0 || end < 0) throw new Error('Map popup builder not found');

  const create = new Function(source.slice(start, end) + '; return buildPinPopupHtml;');
  const buildPopup = create();
  const assert = (condition, message) => { if (!condition) throw new Error(message); };

  const marked = buildPopup({ customer: 'Kunde', isAlternativeLiefertour: true });
  assert(marked.includes('Evtl. Liefertour'), 'Alternative Liefertour must be shown in the popup');

  const regular = buildPopup({ customer: 'Kunde', isAlternativeLiefertour: false });
  assert(!regular.includes('Evtl. Liefertour'), 'Regular orders must not show the alternative Liefertour line');

  const missing = buildPopup({ customer: 'Kunde' });
  assert(!missing.includes('Evtl. Liefertour'), 'Missing flag must not show the alternative Liefertour line');

  const withNotes = buildPopup({
    customer: 'Kunde',
    notes: 'Dringender Hinweis',
    showNotes: true,
    showStreet: true,
    street: 'Teststrasse 1'
  });
  assert(withNotes.includes("class='gawela-info-card-notes'>Dringender Hinweis</p>"), 'Notes must use the dedicated header style');
  assert(withNotes.indexOf('Dringender Hinweis') < withNotes.indexOf('Teststrasse 1'), 'Notes must be rendered before the address');
  assert(!withNotes.includes('>Notizen<'), 'Notes must not have a label');

  const hiddenNotes = buildPopup({ customer: 'Kunde', notes: 'Unsichtbar', showNotes: false });
  assert(!hiddenNotes.includes('Unsichtbar'), 'Hidden notes must not be rendered');

  assert(source.includes("popupEl.classList.add('gawela-order-popup', 'gawela-popup-front')"), 'Activated popup must receive the foreground class');
  assert(source.includes("popupEl.addEventListener('pointerdown', activate)"), 'Popup pointer interaction must activate the popup');
  assert(source.includes('.gawela-order-popup.gawela-popup-front { z-index: 1500 !important; }'), 'Foreground popup must have a higher stack level');
  assert(!source.includes('.gawela-order-popup * { pointer-events: auto !important; }'), 'Transparent popup areas must not intercept marker clicks');
  assert(source.includes('.gawela-order-popup .gawela-info-card * { pointer-events: auto !important; }'), 'Only visible info-card content must accept pointer input');

  const withManyProducts = buildPopup({
    customer: 'Kunde',
    showProducts: true,
    products: ['Produkt 1', 'Produkt 2', 'Produkt 3', 'Produkt 4']
  });
  assert((withManyProducts.match(/gawela-info-card-product-extra/g) || []).length === 2, 'Only products after the first two must initially be hidden');
  assert(withManyProducts.includes("aria-expanded='false'>Mehr anzeigen</button>"), 'More than two products must render the expand button');

  const withTwoProducts = buildPopup({ customer: 'Kunde', showProducts: true, products: ['Produkt 1', 'Produkt 2'] });
  assert(!withTwoProducts.includes('gawela-info-card-products-toggle'), 'Two products must not render the expand button');
  assert(source.includes("toggle.textContent = expanded ? 'Mehr anzeigen' : 'Weniger anzeigen'"), 'Product toggle must support expanding and collapsing');
  assert(source.includes("popupEl.addEventListener('pointerup'"), 'Product toggle must use delegated handling for asynchronously rendered popup content');
  assert(source.includes("target.closest('.gawela-info-card-products-toggle')"), 'Delegated popup handling must resolve the product toggle at interaction time');
  assert(source.includes('popup.setLngLat(popup.getLngLat())'), 'Popup must be repositioned after its height changes');
  assert(source.includes("popupEl.addEventListener('wheel'"), 'Mouse wheel input over an info card must be forwarded to map zoom');
  assert(source.includes("changeMapZoom(evt.deltaY < 0 ? 0.75 : -0.75, around)"), 'Info-card wheel zoom must preserve the pointer position');

  return 21;
}

if (typeof module !== 'undefined') {
  const fs = require('node:fs');
  const path = require('node:path');
  const source = fs.readFileSync(path.join(__dirname, '../src/Tourenplaner.CSharp.App/Views/Sections/MapHtmlDocumentBuilder.cs'), 'utf8');
  console.log(runMapPinPopupTests(source) + ' map popup checks passed');
}
