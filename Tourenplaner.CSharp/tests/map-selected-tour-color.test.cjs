// Run with: node Tourenplaner.CSharp/tests/map-selected-tour-color.test.cjs
// Executes the production selected-tour highlight logic embedded in the map document.
function runSelectedTourColorTests(source) {
  const start = source.indexOf("const plannedTourOverlaysSourceId =");
  const end = source.indexOf('const applyPoiVisibility =', start);
  if (start < 0 || end < 0) throw new Error('Planned-tour highlight logic not found');

  const create = new Function('map', 'mapState', 'tourHoverTooltipEl', 'selectRouteStopNearPoint', 'window',
    source.slice(start, end) +
    '; plannedTourOverlaySelectedId = 7; return applyPlannedTourOverlayHighlight;');
  const assert = (condition, message) => { if (!condition) throw new Error(message); };
  let checks = 0;

  for (const [style, expectedColor] of [['basic', '#2563EB'], ['satellite', '#7DD3FC']]) {
    const paintChanges = [];
    const map = {
      getLayer: () => ({}),
      setFilter: () => {},
      setPaintProperty: (layer, property, value) => paintChanges.push({ layer, property, value })
    };
    const applyHighlight = create(map, { style }, null, () => false, {});
    applyHighlight();
    const colorChange = paintChanges.find(change =>
      change.layer === 'gawela-planned-tour-overlays-selected-layer' &&
      change.property === 'line-color');
    assert(colorChange && colorChange.value === expectedColor, `Selected tour must use ${expectedColor} in ${style} view`);
    checks++;
  }

  return checks;
}

if (typeof module !== 'undefined') {
  const fs = require('node:fs');
  const path = require('node:path');
  const source = fs.readFileSync(path.join(__dirname, '../src/Tourenplaner.CSharp.App/Views/Sections/MapHtmlDocumentBuilder.cs'), 'utf8');
  console.log(runSelectedTourColorTests(source) + ' selected-tour color checks passed');
}
