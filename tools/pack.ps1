param(
  [string]$Version = "0.1.0",
  [string]$OutDir = "dist"
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location (Join-Path $Root '..')

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType Directory -Force -Path 'build/package' | Out-Null

$manifest = Join-Path 'extension' 'extension.yaml'
if (-not (Test-Path $manifest)) { throw "Missing extension/extension.yaml" }

# Copy extension manifest and plugin binaries
Copy-Item $manifest 'build/package/extension.yaml' -Force

$dllCandidates = @(
  'src/OverlayPlugin/bin/Release/net8.0-windows/OverlayPlugin.dll',
  'src/OverlayPlugin/bin/Release/net7.0-windows/OverlayPlugin.dll',
  'src/OverlayPlugin/bin/Release/net6.0-windows/OverlayPlugin.dll'
)

$dll = $dllCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dll) { Write-Warning 'OverlayPlugin.dll not found; packaging manifest only.' }
else { Copy-Item $dll 'build/package/OverlayPlugin.dll' -Force }

$name = "playnite-overlay-$Version.pext"
$zip = Join-Path $OutDir $name
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path 'build/package/*' -DestinationPath $zip
Write-Host "Created: $zip"

