const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const source = fs.readFileSync(path.join(__dirname, '../src/Tourenplaner.CSharp.App/Views/Sections/MapHtmlDocumentBuilder.cs'), 'utf8');
const start = source.indexOf('const updateMarkerScreenClusterState =');
const end = source.indexOf('const zoomToScreenCluster =', start);
assert(start >= 0 && end > start);
const update = new Function('document', source.slice(start, end) + '; return updateMarkerScreenClusterState;')({
  createElement: () => ({ remove() {} })
});
let checks = 0;
for (const id of ['12345', 'manual:test']) {
  for (const route of [false, true]) {
    const element = {
      style: {}, dataset: {}, querySelector: () => null,
      appendChild() {}, setAttribute() {}, removeAttribute() {}
    };
    const marker = {
      draggable: true,
      getElement: () => element,
      setDraggable(value) { this.draggable = value; }
    };
    // Repeated refreshes: cluster, hidden member, individual pin, zoomed out again.
    for (const [count, hidden] of [[3, false], [3, true], [1, false], [0, false], [2, false]]) {
      update(marker, { id, __screenClusterCount: count, __screenClusterHidden: hidden }, route);
      assert.equal(marker.draggable, false, `${id}: dragging must remain disabled after cluster updates`);
      assert.equal(element.style.pointerEvents, hidden ? 'none' : 'auto');
      checks += 2;
    }
  }
}
console.log(`${checks} marker dragging checks passed`);
