# Builds the 3D modelling module: Chili3D (AGPL-3.0) at a pinned commit + MühendisKit overlay
# (Turkish locale, host bridge, analytics tag removed) → src/MuhendisKit/wwwroot/cad3d
param([switch]$Clean)
$ErrorActionPreference = 'Stop'
$Commit = '03a6a542e841a7f1952f67aa729e0658a8096c08'
$Here = $PSScriptRoot
$Src = Join-Path $Here '.src'
$Out = Join-Path $Here '..\..\src\MuhendisKit\wwwroot\cad3d'

if ($Clean -and (Test-Path $Src)) { Remove-Item -Recurse -Force $Src }
if (-not (Test-Path $Src)) {
    git clone --filter=blob:none https://github.com/xiangechen/chili3d $Src
}
git -C $Src fetch --depth 1 origin $Commit
git -C $Src checkout --force $Commit
git -C $Src clean -fdq -e node_modules

# Overlay: files here replace (or add to) the upstream tree at the same relative path.
$Overlay = Join-Path $Here 'overlay'
Get-ChildItem $Overlay -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($Overlay.Length + 1)
    $dest = Join-Path $Src $rel
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    Copy-Item $_.FullName $dest -Force
}

Push-Location $Src
try {
    if (-not (Test-Path node_modules)) { npm ci --no-audit --no-fund }
    $env:NODE_ENV = 'production'
    npx rspack build
    if ($LASTEXITCODE -ne 0) { throw 'rspack build failed' }
} finally { Pop-Location }

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
Copy-Item (Join-Path $Src 'dist') $Out -Recurse
# AGPL: ship the license and say where the corresponding source is.
Copy-Item (Join-Path $Src 'LICENSE') (Join-Path $Out 'LICENSE-Chili3D.txt')
"Chili3D $Commit + MühendisKit overlay (tools/cad3d/overlay). Kaynak: https://github.com/xiangechen/chili3d" | Set-Content (Join-Path $Out 'SOURCE.txt') -Encoding utf8
Write-Host "cad3d → $Out"
