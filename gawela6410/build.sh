#!/usr/bin/env bash
set -euo pipefail

# Restore the verified original 6.4.0 module source.
cat legacy/gawela-build/chunks/part*.txt | tr -d '\r\n' > "$RUNNER_TEMP/module.b64"
base64 -d "$RUNNER_TEMP/module.b64" > "$RUNNER_TEMP/module.zip"
echo "a34f06fc6668a2fd7faff1748a44e68073b9a52fef0204bcb59f02972b3958b9  $RUNNER_TEMP/module.zip" | sha256sum -c -
unzip -t "$RUNNER_TEMP/module.zip"
mkdir -p "$RUNNER_TEMP/module-src"
unzip -q "$RUNNER_TEMP/module.zip" -d "$RUNNER_TEMP/module-src"

rm -rf smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator
cp -R "$RUNNER_TEMP/module-src/Gawela.ColorConfigurator" smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator

# Apply the complete, previously verified upgrade chain.
python3 gawela641/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator
sed -i '1i using Smartstore;' smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Startup.cs
cp -R gawela642/Models smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/
cp -R gawela642/Services smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/
cp -R gawela642/Controllers smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/
cp -R gawela642/Views smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/
cp -R gawela642/wwwroot smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/
cp gawela642/module.json smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/module.json
sed -i 's/"Version": "6.4.2"/"Version": "6.4.3"/' smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/module.json
python3 gawela643/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator
python3 gawela644/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator
cp -R gawela645/Models/* smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Models/
cp -R gawela645/Services/* smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Services/
cp -R gawela645/Controllers/* smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Controllers/
cp gawela645/Views/GawelaColorAdmin/Configure.cshtml smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Views/GawelaColorAdmin/Configure.cshtml
cp gawela645/Views/Shared/Components/GawelaColorHost/Default.cshtml smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Views/Shared/Components/GawelaColorHost/Default.cshtml
cp gawela645/Startup.cs smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Startup.cs
cp gawela645/module.json smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/module.json
python3 gawela646/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator
cp gawela647/Models/GawelaProductGroup.cs smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Models/
cp gawela647/Services/GawelaProductGroupStore.cs smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Services/
python3 gawela647/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator || true
python3 gawela647/finalize_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator
python3 gawela648/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator
python3 gawela649/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator
python3 gawela6410/apply_patch.py smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator

grep -q '"Version": "6.4.10"' smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/module.json
grep -q 'ResolveOwnerProductIdAsync' smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Controllers/GawelaColorController.cs
grep -q 'sku-fallback' smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Controllers/GawelaColorController.cs
grep -q 'X-Gawela-Resolution' smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Controllers/GawelaColorController.cs
grep -q 'public IActionResult Palette()' smartstore/src/Smartstore.Modules/Gawela.ColorConfigurator/Controllers/GawelaColorController.cs

# Build only with the pinned SDK selected by the workflow.
pushd smartstore >/dev/null
dotnet --version
dotnet restore src/Smartstore.Modules/Gawela.ColorConfigurator/Gawela.ColorConfigurator.csproj
dotnet build src/Smartstore.Modules/Gawela.ColorConfigurator/Gawela.ColorConfigurator.csproj -c Release --no-restore
popd >/dev/null

MODULE=smartstore/src/Smartstore.Web/Modules/Gawela.ColorConfigurator
test -f "$MODULE/Gawela.ColorConfigurator.dll"
test -f "$MODULE/module.json"
test -f "$MODULE/wwwroot/gawela-color.js"
test -f "$MODULE/Views/GawelaColorAdmin/Configure.cshtml"
grep -q '"Version": "6.4.10"' "$MODULE/module.json"
grep -q 'Bildgruppe / Höhenvorlage' "$MODULE/Views/GawelaColorAdmin/Configure.cshtml"

# Create the official Smartstore package with PackageBuilder.
mkdir -p smartstore/tools/GawelaPackager
cat > smartstore/tools/GawelaPackager/GawelaPackager.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="../../src/Smartstore.Core/Smartstore.Core.csproj" /></ItemGroup>
</Project>
EOF
cat > smartstore/tools/GawelaPackager/Program.cs <<'EOF'
using Microsoft.Extensions.FileProviders;
using Smartstore.Core.Packaging;
using Smartstore.Engine.Modularity;
using Smartstore.IO;
if (args.Length != 2) return 2;
var root = Path.GetFullPath(args[0]);
var outputFile = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
var builder = new PackageBuilder(new LocalFileSystem(root));
var package = await builder.BuildPackageAsync(new Descriptor(root));
await using var output = File.Create(outputFile);
await package.ArchiveStream.CopyToAsync(output);
return 0;
sealed class Descriptor : IExtensionDescriptor, IExtensionLocation
{
    private readonly string _root; public Descriptor(string root) => _root = root;
    public string Name => "Gawela.ColorConfigurator";
    public ExtensionType ExtensionType => ExtensionType.Module;
    public string FriendlyName => "GAWELA Universeller Produktkonfigurator";
    public string Description => "Attributgesteuerte dynamische RAL-Farbvorschau mit wiederverwendbaren Bildgruppen/Höhenvorlagen, zentral pflegbaren RGB/HEX-Werten und Smartstore-Galerie-Slide.";
    public string Group => "CMS"; public string Author => "GAWELA GmbH"; public string ProjectUrl => string.Empty;
    public string Tags => "GAWELA,RAL,Produktkonfigurator,Höhenvorlagen,Produktgalerie";
    public Version Version => new(6,4,10); public Version MinAppVersion => new(6,4,0);
    public string Path => "/Modules/Gawela.ColorConfigurator";
    public string PhysicalPath => System.IO.Path.Combine(_root,"Modules","Gawela.ColorConfigurator");
    public IFileSystem ContentRoot => new LocalFileSystem(PhysicalPath);
    public IFileProvider WebRoot => new NullFileProvider();
}
EOF

pushd smartstore >/dev/null
dotnet run --project tools/GawelaPackager/GawelaPackager.csproj -c Release -- \
  "$GITHUB_WORKSPACE/smartstore/src/Smartstore.Web" \
  "$GITHUB_WORKSPACE/Smartstore.Module.Gawela.ColorConfigurator.6.4.10.zip"
popd >/dev/null

test -s "$GITHUB_WORKSPACE/Smartstore.Module.Gawela.ColorConfigurator.6.4.10.zip"
unzip -t "$GITHUB_WORKSPACE/Smartstore.Module.Gawela.ColorConfigurator.6.4.10.zip"
sha256sum "$GITHUB_WORKSPACE/Smartstore.Module.Gawela.ColorConfigurator.6.4.10.zip"
