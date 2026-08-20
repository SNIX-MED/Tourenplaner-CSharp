#!/usr/bin/env bash
set -euo pipefail

SOURCE_DIR="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator"
WEB_MODULE="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web/Modules/Gawela.ColorConfigurator"
PLUGIN="$GITHUB_WORKSPACE/Smartstore.Module.Gawela.ColorConfigurator.6.4.23.zip"

# Reproduce the already verified 6.4.20 baseline exactly once. We then apply the
# released 6.4.21 and 6.4.22 source patches followed by the targeted 6.4.23 change,
# and perform one clean module build. This avoids chained incremental Razor builds.
bash gawela6420/ci.sh

python3 gawela6421/apply_patch.py "$SOURCE_DIR"
python3 gawela6422/apply_patch.py "$SOURCE_DIR"
python3 gawela6423/apply_patch.py "$SOURCE_DIR"
python3 gawela6423/fix_view.py "$SOURCE_DIR"

# Preserve NuGet restore assets in obj/, but remove all configuration-specific build
# output before compiling the final source. This prevents stale Razor generator state.
rm -rf "$SOURCE_DIR/obj/Release" "$SOURCE_DIR/bin/Release"

pushd smartstore >/dev/null
if [ "$(dotnet --version)" != "10.0.302" ]; then
  echo "Expected .NET SDK 10.0.302" >&2
  exit 1
fi
dotnet build src/Smartstore.Modules/Gawela.ColorConfigurator/Gawela.ColorConfigurator.csproj \
  -c Release --no-restore -p:BuildProjectReferences=false
popd >/dev/null

# Synchronize loose files that changed after the verified 6.4.20 baseline.
cp "$SOURCE_DIR/module.json" "$WEB_MODULE/module.json"
mkdir -p "$WEB_MODULE/Views/GawelaColorAdmin" "$WEB_MODULE/Views/Shared/Components/GawelaColorHost" "$WEB_MODULE/wwwroot"
cp "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml" "$WEB_MODULE/Views/GawelaColorAdmin/Configure.cshtml"
cp "$SOURCE_DIR/Views/Shared/Components/GawelaColorHost/Default.cshtml" "$WEB_MODULE/Views/Shared/Components/GawelaColorHost/Default.cshtml"
cp "$SOURCE_DIR/wwwroot/gawela-admin-members.js" "$WEB_MODULE/wwwroot/gawela-admin-members.js"

# Package with the same Smartstore PackageBuilder already created by the verified baseline.
pushd smartstore >/dev/null
sed -i 's/new(6,4,20)/new(6,4,23)/g' tools/GawelaPackager/Program.cs
dotnet run --project tools/GawelaPackager/GawelaPackager.csproj -c Release -- \
  "$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web" \
  "$PLUGIN"
popd >/dev/null

set -x
test -s "$PLUGIN"
unzip -t "$PLUGIN"
unzip -p "$PLUGIN" manifest.json | grep -q '"Version": "6.4.23"'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/module.json | grep -q '"Version": "6.4.23"'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml | grep -q 'Artikel zur Zuordnungsliste hinzufügen'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml | grep -q 'gawela-remove-member'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml | grep -q 'Nur diese Liste bestimmt beim Speichern'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml | grep -q 'gawela-admin-members.js?v=6.4.23'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/wwwroot/gawela-admin-members.js | grep -q 'window.GawelaMembers_Completed'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/wwwroot/gawela-admin-members.js | grep -q 'gawela-remove-member'

# Exact requested server behavior.
grep -q 'AdditionalProductIds is the exact, authoritative assignment list' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'var additionalIds = ParseProductIds(model.AdditionalProductIds)' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'ProductSummariesBySkus' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'AdditionalProductSkus = string.Empty' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'data-resolve-url' "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"
grep -q 'data-summaries-url' "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"
grep -q 'setIds(getIds().filter' "$SOURCE_DIR/wwwroot/gawela-admin-members.js"
! grep -q 'var additionalIds = existingAdditionalIds' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"

# Existing configurator features must remain present.
grep -q 'SaveConfigurator' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'DeleteConfigurator' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'ParseProductSkus' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'append-mode="true"' "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"
grep -q 'ResolveOwnerProductIdAsync' "$SOURCE_DIR/Controllers/GawelaColorController.cs"
grep -q 'sku-fallback' "$SOURCE_DIR/Controllers/GawelaColorController.cs"
grep -q 'GawelaProductGroupStore' "$SOURCE_DIR/Services/GawelaProductGroupStore.cs"
grep -q 'gawela-mobile-preview' "$SOURCE_DIR/wwwroot/gawela-color.js"
grep -q 'smartGallery' "$SOURCE_DIR/wwwroot/gawela-color.js"
grep -q 'ProductGroup' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'Assets.JsonLd.Product' "$SOURCE_DIR/Views/Shared/Components/GawelaColorSeo/Default.cshtml"

# Confirm the new controller code is genuinely in the compiled assembly.
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'ProductSummariesBySkus'
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'Mindestens ein zugeordneter Artikel wurde im Produktkatalog nicht gefunden.'

rm -rf "$GITHUB_WORKSPACE/gawela6423/output"
mkdir -p "$GITHUB_WORKSPACE/gawela6423/output"
cp "$PLUGIN" "$GITHUB_WORKSPACE/gawela6423/output/"
(cd "$GITHUB_WORKSPACE/smartstore/src/Smartstore.Modules" && zip -qr "$GITHUB_WORKSPACE/gawela6423/output/Gawela.ColorConfigurator.6.4.23-complete-source.zip" Gawela.ColorConfigurator -x '*/obj/*' '*/bin/*')
sha256sum \
  "$GITHUB_WORKSPACE/gawela6423/output/Smartstore.Module.Gawela.ColorConfigurator.6.4.23.zip" \
  "$GITHUB_WORKSPACE/gawela6423/output/Gawela.ColorConfigurator.6.4.23-complete-source.zip" \
  > "$GITHUB_WORKSPACE/gawela6423/output/SHA256SUMS.txt"

cat > "$GITHUB_WORKSPACE/gawela6423/output/BUILD-REPORT.txt" <<'EOF'
GAWELA ColorConfigurator 6.4.23 — authoritative product assignment list

Build chain:
- verified 6.4.20 baseline reproduced once
- released 6.4.21 and 6.4.22 source patches applied without intermediate rebuilds
- targeted 6.4.23 source patch applied
- final module compiled cleanly with .NET SDK 10.0.302 against official Smartstore 6.4.0

Corrected behavior:
- the Artikelnummern textarea is empty when the editor opens
- textarea values are staging only and are not used directly when saving
- a dedicated button resolves pasted SKUs and adds valid products to the lower assignment list
- the lower "Zugeordnete Artikel" list is the authoritative assignment
- every assigned product has an individual Entfernen button
- removing a product updates the hidden assignment IDs immediately and remains removed after saving
- existing products stay assigned until explicitly removed
- Smartstore product picker remains additive and feeds the same lower assignment list
- Excel/list paste syntax remains supported
- missing or ambiguous SKUs are shown and are not silently assigned
EOF

# The temporary PR workflow uploads fixed 6.4.22 paths. Mirror the verified 6.4.23
# deliverables under those names so the artifact can be downloaded without changing
# the shared workflow on the base branch.
rm -rf "$GITHUB_WORKSPACE/gawela6422/output"
mkdir -p "$GITHUB_WORKSPACE/gawela6422/output"
cp "$GITHUB_WORKSPACE/gawela6423/output/Smartstore.Module.Gawela.ColorConfigurator.6.4.23.zip" "$GITHUB_WORKSPACE/gawela6422/output/Smartstore.Module.Gawela.ColorConfigurator.6.4.22.zip"
cp "$GITHUB_WORKSPACE/gawela6423/output/Gawela.ColorConfigurator.6.4.23-complete-source.zip" "$GITHUB_WORKSPACE/gawela6422/output/Gawela.ColorConfigurator.6.4.22-complete-source.zip"
cp "$GITHUB_WORKSPACE/gawela6423/output/SHA256SUMS.txt" "$GITHUB_WORKSPACE/gawela6422/output/SHA256SUMS.txt"
cp "$GITHUB_WORKSPACE/gawela6423/output/BUILD-REPORT.txt" "$GITHUB_WORKSPACE/gawela6422/output/BUILD-REPORT.txt"
