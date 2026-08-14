from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1]).resolve()
patch_dir = Path(__file__).resolve().parent
source = patch_dir / 'source'

files = [
    'Models/GawelaProductConfig.cs',
    'Models/GawelaAssetAdminModel.cs',
    'Services/GawelaAssetStore.cs',
    'Controllers/GawelaColorAdminController.cs',
    'Controllers/GawelaColorController.cs',
    'Views/GawelaColorAdmin/Configure.cshtml',
    'Views/Shared/Components/GawelaColorHost/Default.cshtml',
    'Views/Shared/Components/GawelaColorSeo/Default.cshtml',
    'wwwroot/gawela-color.js',
    'wwwroot/gawela-color.css',
]

for rel in files:
    src = source / rel
    dst = root / rel
    if not src.exists():
        raise SystemExit(f'Missing 6.4.15 source file: {rel}')
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)

module = root / 'module.json'
text = module.read_text(encoding='utf-8')
if '"Version": "6.4.11"' not in text:
    raise SystemExit('Expected 6.4.11 module version before applying 6.4.15 patch.')
text = text.replace('"Version": "6.4.11"', '"Version": "6.4.15"', 1)
module.write_text(text, encoding='utf-8')

checks = {
    'Models/GawelaProductConfig.cs': ['BaseMediaFileId', 'MaskMediaFileId', 'public string Name'],
    'Models/GawelaAssetAdminModel.cs': ['UIHint("Media")', 'ConfiguratorName', 'GawelaConfiguratorOverviewModel'],
    'Controllers/GawelaColorAdminController.cs': ['SaveConfigurator', 'DeleteConfigurator', 'IMediaService', 'ProductSummaries'],
    'Controllers/GawelaColorController.cs': ['GetMediaFileId', 'OpenReadAsync', 'sku-fallback'],
    'Views/GawelaColorAdmin/Configure.cshtml': ['Farbkonfiguratoren', 'Hinzufügen', 'Basis-Artikel auswählen', '<editor asp-for="BaseMediaFileId"', '<entity-picker'],
    'Views/Shared/Components/GawelaColorHost/Default.cshtml': ['v=6.4.15'],
    'wwwroot/gawela-color.js': ['gawela-mobile-preview', 'silent'],
}
for rel, needles in checks.items():
    data = (root / rel).read_text(encoding='utf-8')
    for needle in needles:
        if needle not in data:
            raise SystemExit(f'6.4.15 verification failed: {needle!r} missing in {rel}')
