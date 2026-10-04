# 次のビルド番号を決める。
#
# 情報源を全部見て、その最大値 + 1 を返す。
# 以前は「インストール済み exe」と buildno.txt の 2 つだけを見ていたため、
# アンインストール済み かつ buildno.txt が消えた状態で 1 に戻ってしまった。
# レジストリに控えを持たせ、掃除ツールがファイルを消しても後戻りしないようにする。
#
# 番号は「系列」（版番号の前 2 つ、1.0 や 1.1）ごとに数える。build.cmd の LINE を
# 1.1 に変えると、1.1 の系列には控えが何も無いので 1.1.0 から始まる。
# 1.0 系列の控えはそのまま残るが、もう読まれない。

param(
    [string]$Dir = $PSScriptRoot,
    [ValidatePattern('^\d+\.\d+$')] [string]$Line = '1.0'
)

$key = 'HKCU:\Software\K-S System\Voyager'
# 1.0 系列はこれまでの名前（LastBuild）をそのまま使う。
$valueName = if ($Line -eq '1.0') { 'LastBuild' } else { "LastBuild-$Line" }
$n   = @(-1)   # 何も見つからなければ 0 から

# 1) レジストリの控え（ファイル掃除の影響を受けない）
try { $n += [int](Get-ItemProperty -Path $key -Name $valueName -ErrorAction Stop).$valueName } catch { }

# 2) 実際にインストールされている exe（同じ系列のときだけ）
$exe = Join-Path $env:LOCALAPPDATA 'Programs\Voyager\Voyager.exe'
if (Test-Path $exe) {
    try {
        $v = (Get-Item $exe).VersionInfo.FileVersionRaw
        if ("$($v.Major).$($v.Minor)" -eq $Line) { $n += [int]$v.Build }
    } catch { }
}

# 3) buildno.txt（1 行目が「系列.番号」。昔の形式の「番号」だけなら 1.0 系列とみなす）
$f = Join-Path $Dir 'buildno.txt'
if (Test-Path $f) {
    $t = (Get-Content $f -TotalCount 1).Trim()
    if ($t -match '^[0-9]+$') { $t = "1.0.$t" }
    if ($t -match '^(\d+\.\d+)\.(\d+)$' -and $Matches[1] -eq $Line) { $n += [int]$Matches[2] }
}

# 4) フォルダに残っている成果物のファイル名（msi / wixpdb）
$prefix = 'Voyager-' + $Line + '.'
Get-ChildItem -LiteralPath $Dir -Filter "$prefix*-x64*" -ErrorAction SilentlyContinue |
    ForEach-Object {
        if ($_.Name -match ('^Voyager-' + [regex]::Escape($Line) + '\.(\d+)-x64')) { $n += [int]$Matches[1] }
    }

$next = ($n | Measure-Object -Maximum).Maximum + 1

# 控えを両方に残す
if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
Set-ItemProperty -Path $key -Name $valueName -Value $next -Type DWord
Set-Content -LiteralPath $f -Value "$Line.$next" -Encoding Ascii

Write-Output $next
