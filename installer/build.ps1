# Builds dist\MuhendisKit-Setup-<version>.exe: web modules, self-contained publish, Inno Setup.
#   pwsh installer\build.ps1 [-SkipWeb]
param([switch]$SkipWeb)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'src\MuhendisKit\MuhendisKit.csproj'
$mcp = Join-Path $root 'src\MuhendisKit.Mcp\MuhendisKit.Mcp.csproj'
$version = ([xml](Get-Content $app)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$publish = Join-Path $root 'publish\app'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 bulunamadı: winget install JRSoftware.InnoSetup' }

if (-not $SkipWeb) { & (Join-Path $root 'tools\build-web.ps1') }
foreach ($m in 'cad2d', 'cad3d') {
    if (-not (Test-Path (Join-Path $root "src\MuhendisKit\wwwroot\$m\index.html"))) { throw "wwwroot\$m yok: önce tools\build-web.ps1" }
}

Write-Host "MühendisKit $version yayınlanıyor..."
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
# Self-contained: target PCs need no .NET. The MCP server is published self-contained into the same folder
# (same runtime version), so Claude can start it on a PC without .NET as well.
$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $publish, '-p:DebugType=none', '-p:DebugSymbols=false', '-nologo', '-v', 'q')
& $dotnet publish $app @common
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (uygulama) başarısız' }
& $dotnet publish $mcp @common
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (MCP) başarısız' }

$bootstrapper = Join-Path $PSScriptRoot 'MicrosoftEdgeWebview2Setup.exe'
if (-not (Test-Path $bootstrapper)) {
    Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper
}
$sig = Get-AuthenticodeSignature $bootstrapper
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'WebView2 kurulumunun imzası geçersiz' }

Write-Host 'Kurulum paketi derleniyor...'
& $iscc /Q "/DAppVersion=$version" (Join-Path $PSScriptRoot 'muhendiskit.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup derlemesi başarısız' }

$out = Join-Path $root "dist\MuhendisKit-Setup-$version.exe"
$size = [math]::Round((Get-Item $out).Length / 1MB, 1)
Write-Host "Hazır: $out ($size MB)"
Get-FileHash $out -Algorithm SHA256 | ForEach-Object { Write-Host "SHA256: $($_.Hash)" }
