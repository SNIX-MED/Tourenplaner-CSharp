#!/usr/bin/env bash
set -euo pipefail

python3 - <<'PY'
from pathlib import Path
src = Path('gawela6410/build.sh').read_text(encoding='utf-8')
needle = 'python3 gawela6410/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator\n'
if needle not in src:
    raise SystemExit('6.4.10 patch insertion point missing')
upgrade = needle + (
    'python3 gawela6411/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator\n'
    'python3 gawela6415/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator\n'
    'python3 gawela6416/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator\n'
    'python3 gawela6419/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator\n'
    'python3 gawela6420/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator\n'
)
src = src.replace(needle, upgrade, 1)
src = src.replace('6.4.10', '6.4.20')
src = src.replace('new(6,4,10)', 'new(6,4,20)')
src = src.replace(
    'Attributgesteuerte dynamische RAL-Farbvorschau mit wiederverwendbaren Bildgruppen/Höhenvorlagen, zentral pflegbaren RGB/HEX-Werten und Smartstore-Galerie-Slide.',
    'Moderner attributgesteuerter RAL-Farbkonfigurator mit Bildgruppen/Höhenvorlagen, zentraler Palette, Smartstore-Galerie- und Mobilvorschau sowie serverseitigen ProductGroup-Varianten, RAL-Farbnamen und semantischen Farbkombinations-URLs.'
)
legacy = "grep -q 'Bildgruppe / Höhenvorlage' \"$MODULE/Views/GawelaColorAdmin/Configure.cshtml\"\n"
current = "grep -q 'Smartstore-Medienkatalog' \"$MODULE/Views/GawelaColorAdmin/Configure.cshtml\"\n"
if legacy not in src:
    raise SystemExit('Legacy admin verification anchor missing')
src = src.replace(legacy, current, 1)
Path('/tmp/gawela6420-build.sh').write_text(src, encoding='utf-8')
PY

bash /tmp/gawela6420-build.sh

set -x
PLUGIN="$GITHUB_WORKSPACE/Smartstore.Module.Gawela.ColorConfigurator.6.4.20.zip"
SOURCE_DIR="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator"
WEB_MODULE="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web/Modules/Gawela.ColorConfigurator"

test -s "$PLUGIN"
unzip -t "$PLUGIN"
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/module.json | grep -q '"Version": "6.4.20"'

# Customer-visible contract.
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/wwwroot/gawela-color.js | grep -q "labels.push(layer.name + ': RAL '"
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/wwwroot/gawela-color.js | grep -q 'Bildschirmdarstellung unverbindlich; Farbe, Proportionen, Details und Ausführung können abweichen.'
! unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/wwwroot/gawela-color.js | grep -q 'Farbabweichungen sind möglich. Die Abbildung dient'
! grep -q '<section' "$SOURCE_DIR/Views/Shared/Components/GawelaColorSeo/Default.cshtml"
! grep -q '<h2' "$SOURCE_DIR/Views/Shared/Components/GawelaColorSeo/Default.cshtml"

# Existing feature regression markers.
grep -q 'SaveConfigurator' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'DeleteConfigurator' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'ProductSummaries' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'ResolveOwnerProductIdAsync' "$SOURCE_DIR/Controllers/GawelaColorController.cs"
grep -q 'sku-fallback' "$SOURCE_DIR/Controllers/GawelaColorController.cs"
grep -q 'GawelaProductGroupStore' "$SOURCE_DIR/Services/GawelaProductGroupStore.cs"
grep -q 'function draw(state)' "$SOURCE_DIR/wwwroot/gawela-color.js"
grep -q 'function tinted' "$SOURCE_DIR/wwwroot/gawela-color.js"
grep -q 'gawela-mobile-preview' "$SOURCE_DIR/wwwroot/gawela-color.js"
grep -q 'smartGallery' "$SOURCE_DIR/wwwroot/gawela-color.js"

# Server-rendered variant semantics and Smartstore Product enrichment.
grep -q 'ProductGroup' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'hasVariant' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'variesBy' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'WebApplication' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'BuildControlId()' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'ProductVariantAttributeValueId' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'BuildVariantProductId' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'inProductGroupWithID' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'farbe-' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'Assets.JsonLd.Product' "$SOURCE_DIR/Views/Shared/Components/GawelaColorSeo/Default.cshtml"
grep -q 'application/ld+json' "$SOURCE_DIR/Views/Shared/Components/GawelaColorSeo/Default.cshtml"
grep -q 'syncSemanticUrl(state)' "$SOURCE_DIR/wwwroot/gawela-color.js"

# Confirm new C# literals are in the compiled DLL.
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'ProductGroup'
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'WebApplication'
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'gawela-ral-farbkonfigurator'
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'inProductGroupWithID'

rm -rf "$GITHUB_WORKSPACE/gawela6420/output"
mkdir -p "$GITHUB_WORKSPACE/gawela6420/output"
cp "$PLUGIN" "$GITHUB_WORKSPACE/gawela6420/output/"
(cd "$GITHUB_WORKSPACE/smartstore/src/Smartstore.Modules" && zip -qr "$GITHUB_WORKSPACE/gawela6420/output/Gawela.ColorConfigurator.6.4.20-complete-source.zip" Gawela.ColorConfigurator)
sha256sum \
  "$GITHUB_WORKSPACE/gawela6420/output/Smartstore.Module.Gawela.ColorConfigurator.6.4.20.zip" \
  "$GITHUB_WORKSPACE/gawela6420/output/Gawela.ColorConfigurator.6.4.20-complete-source.zip" \
  > "$GITHUB_WORKSPACE/gawela6420/output/SHA256SUMS.txt"

cat > "$GITHUB_WORKSPACE/gawela6420/output/BUILD-REPORT.txt" <<'EOF'
GAWELA ColorConfigurator 6.4.20 — complete C# source rebuild

Build base:
- verified original GAWELA 6.4.0 C# source archive
- retained upgrade chain through 6.4.16, plus 6.4.19/6.4.20
- official Smartstore 6.4.0 source
- .NET SDK 10.0.302

Preserved/regression-checked:
- admin configurator management and deletion
- asset/mask configuration and image compositing
- product summaries
- reusable product groups and SKU fallback
- RAL palette
- live Smartstore gallery integration
- mobile preview

Customer-visible output:
- selected product areas and RAL values remain visible
- exact short disclaimer remains visible
- no additional visible SEO/AI marketing block

Machine-readable improvements:
- real RAL names resolved server-side
- real Smartstore ProductVariantAttribute/ProductVariantAttributeValue IDs
- real pvari query keys through Smartstore BuildControlId()
- real Cartesian RAL combinations under ProductGroup/hasVariant
- deterministic unique productID for every represented colour combination
- exact functional Smartstore variant URLs plus semantic farbe-... aliases
- current variant enriches Smartstore's own Product JSON-LD, preserving its real Offer/price/availability data
- WebApplication structured data for the interactive configurator
- no fabricated price, SKU or GTIN; no hidden keyword text/cloaking
EOF
