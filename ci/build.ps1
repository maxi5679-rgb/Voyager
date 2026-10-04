# SPDX-License-Identifier: GPL-3.0-or-later
<#
  Voyager: build both MSIs and the Store MSIX without installing anything.

  This is the part of build.cmd that GitHub Actions can run: publish, stage and
  package. build.cmd keeps the rest (choosing the next number from what is
  installed on this PC, closing the running app, installing, debug.on), which
  only makes sense on the developer's own machine.

    pwsh ci/build.ps1 -Version 1.0.95
    pwsh ci/build.ps1 -Version 1.0.95 -OutDir out

  Writes Voyager-<ver>-x64.msi (English), Voyager-<ver>-x64-ja.msi (Japanese)
  and Voyager-<ver>-x64.msix (unsigned, for the Microsoft Store) into -OutDir,
  and prints their size and SHA-256. The MSIX step needs the Windows SDK
  (makeappx / makepri), which GitHub's Windows runners have.
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

Step "[4] msix $Version"
# The Store build. Unsigned: Partner Center signs what it publishes, and an
# unsigned MSIX cannot be installed by double-click, so it never goes on the
# Releases page. Its data lives apart from the MSI build's (src/AppPaths.cs).
$sdkBin = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
          Where-Object { $_.Directory.Parent.Name -match '^\d+(\.\d+){3}$' } |
          Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
Must $sdkBin 'makeappx.exe not found (Windows SDK)'
$makeappx = $sdkBin.FullName
$makepri  = Join-Path $sdkBin.DirectoryName 'makepri.exe'
Must (Test-Path $makepri) "makepri.exe not found next to $makeappx"
Write-Host "sdk: $($sdkBin.DirectoryName)"

$layout = Join-Path $work 'msix'
Copy-Item $pub $layout -Recurse                     # Voyager.exe, .NET, WebView2Loader.dll, assets\
# Images\, not Assets\: the publish output already has assets\voyager.jpg and
# Windows paths ignore case.
Copy-Item (Join-Path $root 'installer\msix\Images') (Join-Path $layout 'Images') -Recurse
$manifest = (Get-Content (Join-Path $root 'installer\msix\AppxManifest.xml') -Raw).Replace('__VERSION__', "$Version.0")
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding($false)))

# resources.pri lets Windows pick Square44x44Logo.targetsize-24.png for the
# taskbar and so on, instead of shrinking one picture. Index only the images.
$priRoot = Join-Path $work 'pri'
New-Item -ItemType Directory -Force $priRoot | Out-Null
Copy-Item (Join-Path $layout 'Images') (Join-Path $priRoot 'Images') -Recurse
Copy-Item (Join-Path $layout 'AppxManifest.xml') $priRoot
$priConfig = Join-Path $work 'priconfig.xml'
& $makepri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Host
Must ($LASTEXITCODE -eq 0) "makepri createconfig failed ($LASTEXITCODE)"
# The default config splits scale-200 into resources.scale-200.pri, which only
# makes sense for bundles with resource packs. One package, one resources.pri.
[xml]$cfg = Get-Content $priConfig -Raw
$cfg.SelectNodes('//packaging') | ForEach-Object { [void]$_.ParentNode.RemoveChild($_) }
$cfg.Save($priConfig)
& $makepri new /pr $priRoot /cf $priConfig /mn (Join-Path $priRoot 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Host
Must ($LASTEXITCODE -eq 0) "makepri new failed ($LASTEXITCODE)"
$extraPri = Get-ChildItem $layout -Filter 'resources.*.pri'
Must (-not $extraPri) "makepri split the resources: $($extraPri.Name -join ', ')"

$msix = Join-Path $out "Voyager-$Version-x64.msix"
& $makeappx pack /d $layout /p $msix /o | Out-Host
Must ($LASTEXITCODE -eq 0) "makeappx pack failed ($LASTEXITCODE)"
Must (Test-Path $msix) 'makeappx reported success but wrote no MSIX'
EndStep

Get-ChildItem $out | Where-Object { $_.Extension -in '.msi', '.msix' } | ForEach-Object {
    $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
    '{0}  {1}  ({2} MB)' -f $h, $_.Name, [math]::Round($_.Length / 1MB, 1)
}
