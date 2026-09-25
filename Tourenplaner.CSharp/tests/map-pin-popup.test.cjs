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

  return 3;
}

if (typeof module !== 'undefined') {
  const fs = require('node:fs');
  const path = require('node:path');
  const source = fs.readFileSync(path.join(__dirname, '../src/Tourenplaner.CSharp.App/Views/Sections/MapHtmlDocumentBuilder.cs'), 'utf8');
  console.log(runMapPinPopupTests(source) + ' map popup checks passed');
}
