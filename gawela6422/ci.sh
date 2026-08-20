#!/usr/bin/env bash
set -euo pipefail

# On the 6.4.23 branch, the base PR workflow still invokes gawela6422/ci.sh.
# Delegate once to the 6.4.23 CI; its nested call sets GAWELA_6423_ACTIVE=1
# so this file then executes the verified 6.4.22 baseline without recursion.
if [ -f gawela6423/ci.sh ] && [ "${GAWELA_6423_ACTIVE:-0}" != "1" ]; then
  export GAWELA_6423_ACTIVE=1
  bash gawela6423/ci.sh
  exit 0
fi

# Pin the exact SDK used by the previously verified GAWELA builds. Hosted runners may
# have a newer 10.0 SDK in PATH; Smartstore 6.4.0 must be compiled with 10.0.302 here.
cat > smartstore/global.json <<'EOF'
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "disable",
    "allowPrerelease": false
  }
}
EOF
if [ "$(cd smartstore && dotnet --version)" != "10.0.302" ]; then
  echo "Expected .NET SDK 10.0.302" >&2
  (cd smartstore && dotnet --info) >&2
  exit 1
fi

# Reproduce and verify the exact 6.4.20 baseline first.
bash gawela6420/ci.sh

SOURCE_DIR="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator"
WEB_MODULE="$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web/Modules/Gawela.ColorConfigurator"
PLUGIN="$GITHUB_WORKSPACE/Smartstore.Module.Gawela.ColorConfigurator.6.4.22.zip"

# Reconstruct the released 6.4.21 source, then snapshot it before this targeted fix.
python3 gawela6421/apply_patch.py "$SOURCE_DIR"
grep -q '"Version": "6.4.21"' "$SOURCE_DIR/module.json"
grep -q 'AdditionalProductSkus' "$SOURCE_DIR/Models/GawelaAssetAdminModel.cs"
grep -q 'direkt aus Excel' "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml"

rm -rf "$RUNNER_TEMP/gawela-6421-source"
cp -R "$SOURCE_DIR" "$RUNNER_TEMP/gawela-6421-source"
rm -rf "$RUNNER_TEMP/gawela-6421-source/obj" "$RUNNER_TEMP/gawela-6421-source/bin"

# Apply only the additive additional-product correction.
python3 gawela6422/apply_patch.py "$SOURCE_DIR"

pushd smartstore >/dev/null
if [ "$(dotnet --version)" != "10.0.302" ]; then
  echo "SDK pin was not honored" >&2
  exit 1
fi
# Dependencies were already built by the verified 6.4.20 baseline. Building only this
# module avoids unrelated Smartstore projects while still compiling C#, Razor views and DLL.
dotnet build src/Smartstore.Modules/Gawela.ColorConfigurator/Gawela.ColorConfigurator.csproj \
  -c Release --no-restore -p:BuildProjectReferences=false
popd >/dev/null

# DeployModule can leave unchanged-content files from the earlier baseline in place during
# a very fast incremental module-only build. Explicitly synchronize the only loose files
# changed by 6.4.22 before packaging; the DLL above is the freshly compiled 6.4.22 assembly.
cp "$SOURCE_DIR/module.json" "$WEB_MODULE/module.json"
mkdir -p "$WEB_MODULE/Views/GawelaColorAdmin" "$WEB_MODULE/Views/Shared/Components/GawelaColorHost"
cp "$SOURCE_DIR/Views/GawelaColorAdmin/Configure.cshtml" "$WEB_MODULE/Views/GawelaColorAdmin/Configure.cshtml"
cp "$SOURCE_DIR/Views/Shared/Components/GawelaColorHost/Default.cshtml" "$WEB_MODULE/Views/Shared/Components/GawelaColorHost/Default.cshtml"

grep -q '"Version": "6.4.22"' "$WEB_MODULE/module.json"
grep -q 'Bestehende Zuordnungen bleiben erhalten' "$WEB_MODULE/Views/GawelaColorAdmin/Configure.cshtml"
grep -q 'v=6.4.22' "$WEB_MODULE/Views/Shared/Components/GawelaColorHost/Default.cshtml"

# Reuse Smartstore's PackageBuilder from the baseline; only advance descriptor version.
pushd smartstore >/dev/null
sed -i 's/new(6,4,20)/new(6,4,22)/g' tools/GawelaPackager/Program.cs
dotnet run --project tools/GawelaPackager/GawelaPackager.csproj -c Release -- \
  "$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web" \
  "$PLUGIN"
popd >/dev/null

set -x
test -s "$PLUGIN"
unzip -t "$PLUGIN"
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/module.json | grep -q '"Version": "6.4.22"'
unzip -p "$PLUGIN" Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml | grep -q 'Bestehende Zuordnungen bleiben erhalten'

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

# Confirm the new controller logic is really present in the freshly compiled assembly.
strings -el "$WEB_MODULE/Gawela.ColorConfigurator.dll" | grep -q 'Folgende neue Artikelnummern wurden nicht gefunden'

# Guard against accidental source changes outside the narrowly requested area.
python3 - <<'PY'
from pathlib import Path
import os, sys
before = Path(os.environ['RUNNER_TEMP']) / 'gawela-6421-source'
after = Path(os.environ['GITHUB_WORKSPACE']) / 'smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator'
allowed = {
    'Controllers/GawelaColorAdminController.cs',
    'Views/GawelaColorAdmin/Configure.cshtml',
    'Views/Shared/Components/GawelaColorHost/Default.cshtml',
    'module.json',
}
changed = set()
all_paths = {p.relative_to(before).as_posix() for p in before.rglob('*') if p.is_file()} | {p.relative_to(after).as_posix() for p in after.rglob('*') if p.is_file()}
for rel in all_paths:
    if rel.startswith('obj/') or rel.startswith('bin/'):
        continue
    a, b = before / rel, after / rel
    if not a.exists() or not b.exists() or a.read_bytes() != b.read_bytes():
        changed.add(rel)
print('Changed vs reconstructed 6.4.21:', sorted(changed))
unexpected = changed - allowed
missing = allowed - changed
if unexpected:
    print('Unexpected changed files:', sorted(unexpected), file=sys.stderr); sys.exit(1)
if missing:
    print('Expected changed files missing:', sorted(missing), file=sys.stderr); sys.exit(1)
PY

rm -rf "$GITHUB_WORKSPACE/gawela6422/output"
mkdir -p "$GITHUB_WORKSPACE/gawela6422/output"
cp "$PLUGIN" "$GITHUB_WORKSPACE/gawela6422/output/"
(cd "$GITHUB_WORKSPACE/smartstore/src/Smartstore.Modules" && zip -qr "$GITHUB_WORKSPACE/gawela6422/output/Gawela.ColorConfigurator.6.4.22-complete-source.zip" Gawela.ColorConfigurator -x '*/obj/*' '*/bin/*')
sha256sum \
  "$GITHUB_WORKSPACE/gawela6422/output/Smartstore.Module.Gawela.ColorConfigurator.6.4.22.zip" \
  "$GITHUB_WORKSPACE/gawela6422/output/Gawela.ColorConfigurator.6.4.22-complete-source.zip" \
  > "$GITHUB_WORKSPACE/gawela6422/output/SHA256SUMS.txt"

cat > "$GITHUB_WORKSPACE/gawela6422/output/BUILD-REPORT.txt" <<'EOF'
GAWELA ColorConfigurator 6.4.22 — additive "Weitere Artikel" correction

Base:
- verified 6.4.20 baseline reproduced first
- released 6.4.21 source reconstructed before applying this fix
- exact .NET SDK 10.0.302 pinned with global.json
- official Smartstore 6.4.0 source

Corrected behavior:
- existing/hinterlegte additional products are always retained when saving
- newly pasted article numbers are appended to the existing assignments
- multiple article numbers can still be pasted from Excel/lists
- existing article numbers in the textarea no longer trigger false duplicate/assignment validation
- only genuinely new articles are checked for conflicts and required attributes
- Smartstore product picker is additive as well
- picker synchronization merges SKUs instead of replacing the textarea contents
- clearing the textarea no longer deletes existing assignments

Change boundary vs reconstructed 6.4.21:
- Controllers/GawelaColorAdminController.cs
- Views/GawelaColorAdmin/Configure.cshtml
- Views/Shared/Components/GawelaColorHost/Default.cshtml (asset version only)
- module.json (version only)

Everything else is byte-identical to the reconstructed 6.4.21 source.
EOF
