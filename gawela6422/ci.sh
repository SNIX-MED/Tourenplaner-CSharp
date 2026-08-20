#!/usr/bin/env bash
set -euo pipefail

# Reproduce and verify the exact released 6.4.21 first.
bash gawela6421/ci.sh

SOURCE_DIR="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator"
WEB_MODULE="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web/Modules/Gawela.ColorConfigurator"
PLUGIN="$GITHUB_WORKSPACE/Smartstore.Module.Gawela.ColorConfigurator.6.4.22.zip"

rm -rf "$RUNNER_TEMP/gawela-6421-source"
cp -R "$SOURCE_DIR" "$RUNNER_TEMP/gawela-6421-source"

# Apply only the additive additional-product correction.
python3 gawela6422/apply_patch.py "$SOURCE_DIR"

pushd smartstore >/dev/null
dotnet --version
dotnet build src/Smartstore.Modules/Gawela.ColorConfigurator/Gawela.ColorConfigurator.csproj -c Release --no-restore

# Reuse Smartstore's PackageBuilder; only advance package descriptor version.
sed -i 's/new(6,4,21)/new(6,4,22)/g' tools/GawelaPackager/Program.cs
dotnet run --project tools/GawelaPackager/GawelaPackager.csproj -c Release -- \
  "$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web" \
  "$PLUGIN"
popd >/dev/null

test -s "$PLUGIN"
unzip -t "$PLUGIN"
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/module.json | grep -q '"Version": "6.4.22"'

# Exact requested behavior.
grep -q 'var existingAdditionalIds = currentMemberIds' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'var additionalIds = existingAdditionalIds' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'additionalIds.Except(existingAdditionalIds)' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'foreach (var member in memberRows.Where(x => newMemberIds.Contains(x.Id)))' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'Folgende neue Artikelnummern wurden nicht gefunden' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'Bestehende Zuordnungen bleiben erhalten' "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"
grep -q 'bereits hinterlegte Artikel bleiben erhalten' "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"
grep -q 'append-mode="true"' "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"
grep -q 'function mergeSkuValues(values)' "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"

# Existing/current members are never run through the new-member conflict loop.
! grep -q 'foreach (var member in memberRows)$' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"

# Regression markers from 6.4.21 / 6.4.20.
grep -q 'SaveConfigurator' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'DeleteConfigurator' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'ParseProductSkus' "$SOURCE_DIR/Controllers/GawelaColorAdminController.cs"
grep -q 'AdditionalProductSkus' "$SOURCE_DIR/Models/GawelaAssetAdminModel.cs"
grep -q 'ResolveOwnerProductIdAsync' "$SOURCE_DIR/Controllers/GawelaColorController.cs"
grep -q 'sku-fallback' "$SOURCE_DIR/Controllers/GawelaColorController.cs"
grep -q 'GawelaProductGroupStore' "$SOURCE_DIR/Services/GawelaProductGroupStore.cs"
grep -q 'gawela-mobile-preview' "$SOURCE_DIR/wwwroot/gawela-color.js"
grep -q 'smartGallery' "$SOURCE_DIR/wwwroot/gawela-color.js"
grep -q 'ProductGroup' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'hasVariant' "$SOURCE_DIR/Components/GawelaColorSeoViewComponent.cs"
grep -q 'Assets.JsonLd.Product' "$SOURCE_DIR/Views/Shared/Components/GawelaColorSeo/Default.cshtml"
grep -q 'Bildschirmdarstellung unverbindlich; Farbe, Proportionen, Details und Ausführung können abweichen.' "$SOURCE_DIR/wwwroot/gawela-color.js"

# Ensure the server-side additive logic is really in the compiled assembly.
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'Folgende neue Artikelnummern wurden nicht gefunden'
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'Mindestens ein im Produktkatalog ausgewählter weiterer Artikel wurde nicht gefunden.'

# Guard against accidental changes outside the narrowly requested area.
python3 - <<'PY'
from pathlib import Path
import filecmp
import sys
before = Path(__import__('os').environ['RUNNER_TEMP']) / 'gawela-6421-source'
after = Path(__import__('os').environ['GITHUB_WORKSPACE']) / 'smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator'
allowed = {
    'Controllers/GawelaColorAdminController.cs',
    'Views/GawelaColorAdmin/Configure.cshtml',
    'Views/Shared/Components/GawelaColorHost/Default.cshtml',
    'module.json',
}
changed = set()
all_paths = {p.relative_to(before).as_posix() for p in before.rglob('*') if p.is_file()} | {p.relative_to(after).as_posix() for p in after.rglob('*') if p.is_file()}
for rel in all_paths:
    a, b = before / rel, after / rel
    if not a.exists() or not b.exists() or a.read_bytes() != b.read_bytes():
        changed.add(rel)
unexpected = changed - allowed
missing = allowed - changed
print('Changed vs 6.4.21:', sorted(changed))
if unexpected:
    print('Unexpected changed files:', sorted(unexpected), file=sys.stderr)
    sys.exit(1)
if missing:
    print('Expected changed files missing:', sorted(missing), file=sys.stderr)
    sys.exit(1)
PY

rm -rf "$GITHUB_WORKSPACE/gawela6422/output"
mkdir -p "$GITHUB_WORKSPACE/gawela6422/output"
cp "$PLUGIN" "$GITHUB_WORKSPACE/gawela6422/output/"
(cd "$GITHUB_WORKSPACE/smartstore/src/Smartstore.Modules" && zip -qr "$GITHUB_WORKSPACE/gawela6422/output/Gawela.ColorConfigurator.6.4.22-complete-source.zip" Gawela.ColorConfigurator)
sha256sum \
  "$GITHUB_WORKSPACE/gawela6422/output/Smartstore.Module.Gawela.ColorConfigurator.6.4.22.zip" \
  "$GITHUB_WORKSPACE/gawela6422/output/Gawela.ColorConfigurator.6.4.22-complete-source.zip" \
  > "$GITHUB_WORKSPACE/gawela6422/output/SHA256SUMS.txt"

cat > "$GITHUB_WORKSPACE/gawela6422/output/BUILD-REPORT.txt" <<'EOF'
GAWELA ColorConfigurator 6.4.22 — additive "Weitere Artikel" correction

Base:
- exact verified 6.4.21 source/build reproduced first
- official Smartstore 6.4.0 source
- .NET SDK 10.0.302

Corrected behavior:
- existing/hinterlegte additional products are always retained when saving
- newly pasted article numbers are appended to the existing assignments
- multiple article numbers can still be pasted from Excel/lists
- existing article numbers in the textarea no longer trigger false duplicate/assignment validation
- only genuinely new articles are checked for conflicts and required attributes
- Smartstore product picker is additive as well
- picker synchronization merges SKUs instead of replacing the textarea contents
- clearing the textarea no longer deletes existing assignments

Change boundary vs 6.4.21:
- Controllers/GawelaColorAdminController.cs
- Views/GawelaColorAdmin/Configure.cshtml
- Views/Shared/Components/GawelaColorHost/Default.cshtml (asset version only)
- module.json (version only)

Everything else is byte-identical to the reproduced 6.4.21 source before compilation.
EOF
