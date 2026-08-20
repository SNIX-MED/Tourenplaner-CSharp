#!/usr/bin/env bash
set -euo pipefail

# First reproduce and verify the exact released 6.4.20 source/build.
bash gawela6420/ci.sh

SOURCE_DIR="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator"
WEB_MODULE="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web/Modules/Gawela.ColorConfigurator"
PLUGIN="$GITHUB_WORKSPACE/Smartstore.Module.Gawela.ColorConfigurator.6.4.21.zip"

# Apply only the requested additional-product input improvement.
python3 gawela6421/apply_patch.py "$SOURCE_DIR"

pushd smartstore >/dev/null
dotnet --version
dotnet build src/Smartstore.Modules/Gawela.ColorConfigurator/Gawela.ColorConfigurator.csproj -c Release --no-restore

# Reuse the official Smartstore PackageBuilder project created by the verified 6.4.20 build,
# changing only the package descriptor version.
sed -i 's/new(6,4,20)/new(6,4,21)/g' tools/GawelaPackager/Program.cs
dotnet run --project tools/GawelaPackager/GawelaPackager.csproj -c Release -- \
  "$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web" \
  "$PLUGIN"
popd >/dev/null

test -s "$PLUGIN"
unzip -t "$PLUGIN"
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/module.json | grep -q '"Version": "6.4.21"'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml | grep -q 'asp-for="AdditionalProductSkus"'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml | grep -q 'direkt aus Excel'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml | grep -q 'Alternativ im Produktkatalog auswählen'

grep -q 'ParseProductSkus' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'NormalizeSkuLookup' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'Folgende Artikelnummern wurden nicht gefunden' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'AdditionalProductSkus' "$SOURCE_DIR/Models/GawelaAssetAdminModel.cs"

# Regression markers: nothing else from 6.4.20 may disappear.
grep -q 'SaveConfigurator' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'DeleteConfigurator' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'ResolveOwnerProductIdAsync' "$SOURCE_DIR/Controllers/GawelaColorController.cs"
grep -q 'sku-fallback' "$SOURCE_DIR/Controllers/GawelaColorController.cs"
grep -q 'GawelaProductGroupStore' "$SOURCE_DIR/Services/GawelaProductGroupStore.cs"
grep -q 'gawela-mobile-preview' "$SOURCE_DIR/wwwroot/gawela-color.js"
grep -q 'smartGallery' "$SOURCE_DIR/wwwroot/gawela-color.js"
grep -q 'ProductGroup' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'hasVariant' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'Assets.JsonLd.Product' "$SOURCE_DIR/Views/Shared/Components/GawelaColorSeo/Default.cshtml"
grep -q 'Bildschirmdarstellung unverbindlich; Farbe, Proportionen, Details und Ausführung können abweichen.' "$SOURCE_DIR/wwwroot/gawela-color.js"

# Ensure the new server-side binding code is really compiled into the DLL.
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'AdditionalProductSkus'
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'Folgende Artikelnummern wurden nicht gefunden'

rm -rf "$GITHUB_WORKSPACE/gawela6421/output"
mkdir -p "$GITHUB_WORKSPACE/gawela6421/output"
cp "$PLUGIN" "$GITHUB_WORKSPACE/gawela6421/output/"
(cd "$GITHUB_WORKSPACE/smartstore/src/Smartstore.Modules" && zip -qr "$GITHUB_WORKSPACE/gawela6421/output/Gawela.ColorConfigurator.6.4.21-complete-source.zip" Gawela.ColorConfigurator)
sha256sum \
  "$GITHUB_WORKSPACE/gawela6421/output/Smartstore.Module.Gawela.ColorConfigurator.6.4.21.zip" \
  "$GITHUB_WORKSPACE/gawela6421/output/Gawela.ColorConfigurator.6.4.21-complete-source.zip" \
  > "$GITHUB_WORKSPACE/gawela6421/output/SHA256SUMS.txt"

cat > "$GITHUB_WORKSPACE/gawela6421/output/BUILD-REPORT.txt" <<'EOF'
GAWELA ColorConfigurator 6.4.21 — targeted additional-product input fix

Base:
- exact verified 6.4.20 source/build reproduced first
- official Smartstore 6.4.0 source
- .NET SDK 10.0.302

Only requested functional change:
- "Weitere Artikel" now has a multiline Artikelnummern field
- multiple article numbers can be pasted directly from Excel or lists
- accepted separators: line break, tab, comma, semicolon, pipe
- existing assigned article numbers are prefilled in the textarea
- empty textarea + save removes all additional products
- unknown article numbers produce a clear validation error
- duplicate SKUs in the Smartstore catalog produce a clear ambiguity error
- the existing Smartstore product picker remains available as an alternative and synchronizes the textarea

Preserved from 6.4.20:
- admin configurator management and deletion
- base product and product-group logic
- asset/mask configuration and image compositing
- RAL palette
- SKU fallback
- live Smartstore gallery integration
- mobile preview
- ProductGroup/hasVariant structured data and semantic colour URLs
- customer-visible disclaimer and existing storefront behavior

No other intentional functional changes were made.
EOF
