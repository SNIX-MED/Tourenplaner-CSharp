param()

$ErrorActionPreference = "Stop"

$innerScript = Join-Path $PSScriptRoot "..\Tourenplaner.CSharp\scripts\start-preview.ps1"
$resolvedInnerScript = (Resolve-Path $innerScript).Path

& $resolvedInnerScript
