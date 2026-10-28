# Builds both editor modules into src/MuhendisKit/wwwroot (run before `dotnet build` / the installer).
#   cad2d: MLightCAD (MIT) + bridge   → wwwroot/cad2d
#   cad3d: Chili3D (AGPL-3.0) + overlay → wwwroot/cad3d
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot 'cad2d')
try {
    if (-not (Test-Path node_modules)) { npm ci --no-audit --no-fund }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'cad2d build failed' }
} finally { Pop-Location }
& (Join-Path $PSScriptRoot 'cad3d\build.ps1')
