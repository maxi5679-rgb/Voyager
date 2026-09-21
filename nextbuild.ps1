# 次のビルド番号を決める。
#
# 情報源を全部見て、その最大値 + 1 を返す。
# 以前は「インストール済み exe」と buildno.txt の 2 つだけを見ていたため、
# アンインストール済み かつ buildno.txt が消えた状態で 1 に戻ってしまった。
# レジストリに控えを持たせ、掃除ツールがファイルを消しても後戻りしないようにする。

param([string]$Dir = $PSScriptRoot)

$key = 'HKCU:\Software\K-S System\Voyager'
$n   = @(0)

# 1) レジストリの控え（ファイル掃除の影響を受けない）
try { $n += [int](Get-ItemProperty -Path $key -Name LastBuild -ErrorAction Stop).LastBuild } catch { }

# 2) 実際にインストールされている exe
$exe = Join-Path $env:LOCALAPPDATA 'Programs\Voyager\Voyager.exe'
if (Test-Path $exe) {
    try { $n += [int](Get-Item $exe).VersionInfo.FileVersionRaw.Build } catch { }
}

# 3) buildno.txt
$f = Join-Path $Dir 'buildno.txt'
if (Test-Path $f) {
    $t = (Get-Content $f -TotalCount 1).Trim()
    if ($t -match '^[0-9]+$') { $n += [int]$t }
}

# 4) フォルダに残っている成果物のファイル名（msi / wixpdb）
Get-ChildItem -LiteralPath $Dir -Filter 'Voyager-1.0.*-x64.*' -ErrorAction SilentlyContinue |
    ForEach-Object {
        if ($_.Name -match '^Voyager-1\.0\.(\d+)-x64\.') { $n += [int]$Matches[1] }
    }

$next = ($n | Measure-Object -Maximum).Maximum + 1

# 控えを両方に残す
if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
Set-ItemProperty -Path $key -Name LastBuild -Value $next -Type DWord
Set-Content -LiteralPath $f -Value $next -Encoding Ascii

Write-Output $next
