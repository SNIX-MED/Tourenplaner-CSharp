#!/usr/bin/env bash
set -euo pipefail

# Reproduce and verify the exact 6.4.22 first.
bash gawela6422/ci.sh

SOURCE_DIR="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator"
WEB_MODULE="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web/Modules/Gawela.ColorConfigurator"
PLUGIN="$GITHUB_WORKSPACE/Smartstore.Module.Gawela.ColorConfigurator.6.4.23.zip"

rm -rf "$RUNNER_TEMP/gawela-6422-source"
cp -R "$SOURCE_DIR" "$RUNNER_TEMP/gawela-6422-source"
rm -rf "$RUNNER_TEMP/gawela-6422-source/obj" "$RUNNER_TEMP/gawela-6422-source/bin"

python3 gawela6423/apply_patch.py "$SOURCE_DIR"

pushd smartstore >/dev/null
if [ "$(dotnet --version)" != "10.0.302" ]; then
  echo "Expected .NET SDK 10.0.302" >&2
  exit 1
fi
dotnet build src/Smartstore.Modules/Gawela.ColorConfigurator/Gawela.ColorConfigurator.csproj \
  -c Release --no-restore -p:BuildProjectReferences=false
popd >/dev/null

cp "$SOURCE_DIR/module.json" "$WEB_MODULE/module.json"
mkdir -p "$WEB_MODULE/Views/GawelaColorAdmin" "$WEB_MODULE/Views/Shared/Components/GawelaColorHost"
cp "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml" "$WEB_MODULE/Views/GawelaColorAdmin/Configure.cshtml"
cp "$SOURCE_DIR/Views/Shared/Components/GawelaColorHost/Default.cshtml" "$WEB_MODULE/Views/Shared/Components/GawelaColorHost/Default.cshtml"

grep -q '"Version": "6.4.23"' "$WEB_MODULE/module.json"
grep -q 'Zugeordnete Artikel' "$WEB_MODULE/Views/GawelaColorAdmin/Configure.cshtml"
grep -q 'gawela-remove-member' "$WEB_MODULE/Views/GawelaColorAdmin/Configure.cshtml"
grep -q 'v=6.4.23' "$WEB_MODULE/Views/Shared/Components/GawelaColorHost/Default.cshtml"

pushd smartstore >/dev/null
sed -i 's/new(6,4,22)/new(6,4,23)/g' tools/GawelaPackager/Program.cs
dotnet run --project tools/GawelaPackager/GawelaPackager.csproj -c Release -- \
  "$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web" \
  "$PLUGIN"
popd >/dev/null

set -x
test -s "$PLUGIN"
unzip -t "$PLUGIN"
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/module.json | grep -q '"Version": "6.4.23"'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml | grep -q 'Artikel zur Zuordnungsliste hinzufügen'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml | grep -q 'gawela-remove-member'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml | grep -q 'Nur diese Liste bestimmt beim Speichern'

grep -q 'AdditionalProductIds is the exact, authoritative assignment list' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'var additionalIds = ParseProductIds(model.AdditionalProductIds)' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'ProductSummariesBySkus' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'AdditionalProductSkus = string.Empty' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'gawela-add-skus' "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"
grep -q 'gawela-remove-member' "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"
grep -q "memberSkusInput.value=''" "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"
grep -q 'setMemberIds(memberIds().filter' "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"

! grep -q 'var additionalIds = existingAdditionalIds' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
! grep -q 'Concat(pastedNewRows' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"

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

strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'ProductSummariesBySkus'
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'Mindestens ein zugeordneter Artikel wurde im Produktkatalog nicht gefunden.'

python3 - <<'PY'
from pathlib import Path
import os, sys
before=Path(os.environ['RUNNER_TEMP'])/'gawela-6422-source'
after=Path(os.environ['GITHUB_WORKSPACE'])/'smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator'
allowed={'Controllers/GawelaColorAdminController.cs','Views/GawelaColorAdmin/Configure.cshtml','Views/Shared/Components/GawelaColorHost/Default.cshtml','module.json'}
changed=set()
paths={p.relative_to(before).as_posix() for p in before.rglob('*') if p.is_file()}|{p.relative_to(after).as_posix() for p in after.rglob('*') if p.is_file()}
for rel in paths:
    if rel.startswith('obj/') or rel.startswith('bin/'): continue
    a,b=before/rel,after/rel
    if not a.exists() or not b.exists() or a.read_bytes()!=b.read_bytes(): changed.add(rel)
print('Changed vs verified 6.4.22:',sorted(changed))
unexpected=changed-allowed
missing=allowed-changed
if unexpected:
    print('Unexpected changed files:',sorted(unexpected),file=sys.stderr);sys.exit(1)
if missing:
    print('Expected changed files missing:',sorted(missing),file=sys.stderr);sys.exit(1)
PY

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

Base:
- exact verified 6.4.22 reproduced first
- official Smartstore 6.4.0 source
- exact .NET SDK 10.0.302

Corrected behavior:
- the Artikelnummern textarea is empty when the editor opens
- textarea values are staging only and are not used directly when saving
- a dedicated button resolves pasted SKUs and adds valid products to the lower assignment list
- the lower "Zugeordnete Artikel" list is the authoritative assignment
- every assigned product has an individual Entfernen button
- removing a product updates the hidden assignment IDs immediately and remains removed after saving
- existing products are preserved until explicitly removed
- Smartstore product picker remains additive and feeds the same lower assignment list
- Excel/list paste syntax remains supported
- missing or ambiguous SKUs are shown and are not silently assigned

Change boundary vs 6.4.22:
- Controllers/GawelaColorAdminController.cs
- Views/GawelaColorAdmin/Configure.cshtml
- Views/Shared/Components/GawelaColorHost/Default.cshtml (asset version only)
- module.json (version only)
EOF

# The temporary PR workflow uploads fixed 6.4.22 paths. Replace those artifact files
# with the already verified 6.4.23 deliverables; they are renamed back after download.
cp "$GITHUB_WORKSPACE/gawela6423/output/Smartstore.Module.Gawela.ColorConfigurator.6.4.23.zip" "$GITHUB_WORKSPACE/gawela6422/output/Smartstore.Module.Gawela.ColorConfigurator.6.4.22.zip"
cp "$GITHUB_WORKSPACE/gawela6423/output/Gawela.ColorConfigurator.6.4.23-complete-source.zip" "$GITHUB_WORKSPACE/gawela6422/output/Gawela.ColorConfigurator.6.4.22-complete-source.zip"
cp "$GITHUB_WORKSPACE/gawela6423/output/SHA256SUMS.txt" "$GITHUB_WORKSPACE/gawela6422/output/SHA256SUMS.txt"
cp "$GITHUB_WORKSPACE/gawela6423/output/BUILD-REPORT.txt" "$GITHUB_WORKSPACE/gawela6422/output/BUILD-REPORT.txt"
