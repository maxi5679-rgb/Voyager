# SPDX-License-Identifier: GPL-3.0-or-later
<#
  Voyager: build both MSIs without installing anything.

  This is the part of build.cmd that GitHub Actions can run: publish, stage and
  package. build.cmd keeps the rest (choosing the next number from what is
  installed on this PC, closing the running app, installing, debug.on), which
  only makes sense on the developer's own machine.

    pwsh ci/build.ps1 -Version 1.0.95
    pwsh ci/build.ps1 -Version 1.0.95 -OutDir out

  Writes Voyager-<ver>-x64.msi (English) and Voyager-<ver>-x64-ja.msi
  (Japanese) into -OutDir, and prints their size and SHA-256.
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+$')] [string] $Version,
    [string] $OutDir = 'out'
)

$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot
$work  = Join-Path $root 'ci-work'
$pub   = Join-Path $work 'publish'
$stage = Join-Path $work 'stage'
$out   = if ([IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $root $OutDir }

function Step($text) { Write-Host "::group::$text" }
function EndStep { Write-Host '::endgroup::' }
function Must($ok, $text) { if (-not $ok) { throw $text } }

foreach ($d in $work, $out) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Force $pub, $stage, $out | Out-Null

Step "[1] publish $Version"
# Self-contained but NOT a single file (see build.cmd: one opaque 43 MB exe made
# the resident scanner read all of it on first launch).
dotnet publish (Join-Path $root 'Voyager.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -p:Version=$Version -o $pub
Must ($LASTEXITCODE -eq 0) "dotnet publish failed ($LASTEXITCODE)"
Must (Test-Path (Join-Path $pub 'Voyager.exe')) 'publish produced no Voyager.exe'

# WebView2Loader.dll must sit next to the exe, not only under runtimes\.
$loader = Join-Path $pub 'WebView2Loader.dll'
if (-not (Test-Path $loader)) {
    $found = Get-ChildItem (Join-Path $pub 'runtimes') -Recurse -Filter WebView2Loader.dll |
             Where-Object { $_.FullName -match 'win-x64' } | Select-Object -First 1
    Must $found 'WebView2Loader.dll not found in publish output'
    Copy-Item $found.FullName $loader
}
Remove-Item (Join-Path $pub 'runtimes') -Recurse -Force -ErrorAction SilentlyContinue
$fv = (Get-Item (Join-Path $pub 'Voyager.exe')).VersionInfo.FileVersion
Write-Host "built FileVersion=$fv"
Must ($fv -eq "$Version.0") "FileVersion $fv does not match $Version"
EndStep

Step '[2] stage'
Copy-Item (Join-Path $pub 'Voyager.exe')        $stage
Copy-Item $loader                                $stage
Copy-Item $pub (Join-Path $stage 'publish') -Recurse
Copy-Item (Join-Path $root 'assets\Voyager.ico')       $stage
Copy-Item (Join-Path $root 'installer\ui\side.bmp')    $stage
Copy-Item (Join-Path $root 'installer\ui\banner.bmp')  $stage
Copy-Item (Join-Path $root 'installer\Voyager.ui.wxs') $stage
Write-Host ("staged files=" + (Get-ChildItem -Recurse -File (Join-Path $stage 'publish')).Count)
EndStep

Step "[3] wix build $Version"
# Two packages from the same source: Windows Installer bakes its own strings
# into the package from Package/@Language, so each language needs its own MSI.
$wxs = Join-Path $stage 'Voyager.ui.wxs'
foreach ($p in @(@{ Lang = 1033; Name = "Voyager-$Version-x64.msi" },
                 @{ Lang = 1041; Name = "Voyager-$Version-x64-ja.msi" })) {
    $msi = Join-Path $out $p.Name
    wix build -arch x64 -d "Ver=$Version" -d "Lang=$($p.Lang)" -d "Stage=$stage" -b $stage -o $msi $wxs
    Must ($LASTEXITCODE -eq 0) "wix build $($p.Lang) failed ($LASTEXITCODE)"
    Must (Test-Path $msi) "wix reported success but wrote no $($p.Name)"
}
Get-ChildItem $out -Filter *.wixpdb | Remove-Item
EndStep

Get-ChildItem $out -Filter *.msi | ForEach-Object {
    $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
    '{0}  {1}  ({2} MB)' -f $h, $_.Name, [math]::Round($_.Length / 1MB, 1)
}
