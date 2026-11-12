<#
.SYNOPSIS
  Builds the Microsoft Store package (.msix) for OG Converter.

.DESCRIPTION
  1. Publishes the same self-contained single-file exe as the GitHub release (release.yml).
  2. Lays it out with AppxManifest.xml and the logo assets from this folder.
  3. Indexes the scaled logos into resources.pri (MakePri) and packs the .msix (MakeAppx).

  The package is unsigned: Partner Center signs Store submissions. Upload
  dist/store/OGConverter_<version>_x64.msix in Partner Center > Packages.

  Needs the .NET 10 SDK and the Windows 10/11 SDK (MakeAppx.exe, MakePri.exe).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File packaging/msix/build-msix.ps1 -Version 1.8.0
#>
param(
    # App version, like the release tag without the v (1.8.0). Defaults to the latest v*.*.* tag.
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Resolve-Path (Join-Path $here '..\..')

if (-not $Version) {
    $tag = git -C $root describe --tags --abbrev=0 --match 'v*.*.*'
    $Version = $tag.TrimStart('v')
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' must look like 1.8.0" }
$pkgVersion = "$Version.0" # the Store requires the fourth part to be 0

# Windows SDK tools: newest installed version.
$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$sdk = Get-ChildItem $kits -Directory -Filter '10.*' | Sort-Object { [version]$_.Name } -Descending |
    Where-Object { Test-Path (Join-Path $_.FullName 'x64\makeappx.exe') } | Select-Object -First 1
if (-not $sdk) { throw "Windows SDK not found under $kits (needs MakeAppx.exe)." }
$makeappx = Join-Path $sdk.FullName 'x64\makeappx.exe'
$makepri = Join-Path $sdk.FullName 'x64\makepri.exe'

$work = Join-Path $root 'artifacts\msix'
$publish = Join-Path $work 'publish'
$layout = Join-Path $work 'layout'
$outDir = Join-Path $root 'dist\store'
$msix = Join-Path $outDir "OGConverter_${pkgVersion}_x64.msix"
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $layout, $outDir | Out-Null

Write-Host "Publishing OG Converter $Version (win-x64, self-contained, single file)..."
dotnet publish (Join-Path $root 'src\OgConverter.Windows\OgConverter.Windows.csproj') `
    -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

Write-Host "Laying out the package..."
Get-ChildItem $publish -Exclude '*.pdb' | Copy-Item -Destination $layout -Recurse
Copy-Item (Join-Path $here 'Assets') -Destination $layout -Recurse -Force
(Get-Content (Join-Path $here 'AppxManifest.xml') -Raw).Replace('__VERSION__', $pkgVersion) |
    Set-Content (Join-Path $layout 'AppxManifest.xml') -Encoding UTF8

Write-Host "Indexing resources (MakePri)..."
$priConfig = Join-Path $work 'priconfig.xml'
& $makepri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makepri createconfig failed ($LASTEXITCODE)." }
& $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makepri new failed ($LASTEXITCODE)." }

Write-Host "Packing (MakeAppx)..."
& $makeappx pack /d $layout /p $msix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed ($LASTEXITCODE)." }

$hash = (Get-FileHash $msix -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $msix -Leaf)" | Out-File -Encoding ascii "$msix.sha256"
$size = [math]::Round((Get-Item $msix).Length / 1MB, 1)
Write-Host ""
Write-Host "Built $msix ($size MB)"
Write-Host "Identity DrAtomic.OGConverter $pkgVersion, x64. Unsigned: upload it in Partner Center, which signs it."
