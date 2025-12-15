param(
  [string]$Version = "0.1.0",
  [string]$OutDir = "dist",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location (Join-Path $Root '..')

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if (Test-Path 'build/package') { Remove-Item 'build/package' -Recurse -Force }
New-Item -ItemType Directory -Force -Path 'build/package' | Out-Null

$manifest = Join-Path 'extension' 'extension.yaml'
if (-not (Test-Path $manifest)) { throw "Missing extension/extension.yaml" }

# Copy extension manifest and icon
Copy-Item $manifest 'build/package/extension.yaml' -Force
$icon = Join-Path 'extension' 'icon.png'
if (Test-Path $icon) { Copy-Item $icon 'build/package/icon.png' -Force }

$dllCandidates = @(
  "src/OverlayPlugin/bin/$Configuration/net472/OverlayPlugin.dll",
  "src/OverlayPlugin/bin/$Configuration/net48/OverlayPlugin.dll",
  "src/OverlayPlugin/bin/$Configuration/net8.0-windows/OverlayPlugin.dll",
  "src/OverlayPlugin/bin/$Configuration/net7.0-windows/OverlayPlugin.dll",
  "src/OverlayPlugin/bin/$Configuration/net6.0-windows/OverlayPlugin.dll"
)

$dll = $dllCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dll) {
  Write-Warning 'OverlayPlugin.dll not found; packaging manifest only.'
}
else {
  $bindir = Split-Path $dll -Parent
  # Copy main DLL
  Copy-Item $dll 'build/package/OverlayPlugin.dll' -Force
  # Copy runtime dependencies (exclude Playnite SDK to avoid conflicts)
  Get-ChildItem $bindir -Filter *.dll | Where-Object { $_.Name -ne 'OverlayPlugin.dll' -and $_.Name -ne 'Playnite.SDK.dll' } | ForEach-Object {
    Copy-Item $_.FullName (Join-Path 'build/package' $_.Name) -Force
  }
  # Include PDBs for debugging (optional)
  Get-ChildItem $bindir -Filter *.pdb -ErrorAction SilentlyContinue | ForEach-Object {
    Copy-Item $_.FullName (Join-Path 'build/package' $_.Name) -Force
  }
}

$name = "playnite-overlay-$Version.pext"
$zip = Join-Path $OutDir $name
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path 'build/package/*' -DestinationPath $zip
Write-Host "Created: $zip"
