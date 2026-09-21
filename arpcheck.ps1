# 「アプリと機能」に登録されている Voyager を数える。
#
# Windows Installer の COM API（ProductsEx）は PowerShell からの遅延束縛で
# 素直に呼べないため使わない。per-user インストールの登録はレジストリの
# HKCU 側に入るので、HKCU と HKLM の両方を見れば取りこぼさない。

param([string]$Label = '')

$found = @()
foreach ($root in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
                    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
                    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')) {
    Get-ChildItem -Path $root -ErrorAction SilentlyContinue | ForEach-Object {
        $p = Get-ItemProperty -Path $_.PSPath -ErrorAction SilentlyContinue
        if ($p.DisplayName -eq 'Voyager') {
            $found += [pscustomobject]@{
                Version = $p.DisplayVersion
                Code    = $_.PSChildName
                Hive    = if ($root.StartsWith('HKCU')) { 'HKCU' } else { 'HKLM' }
            }
        }
    }
}

Write-Output ("  registered Voyager ($Label): " + $found.Count + " 件")
if ($found.Count -eq 0) {
    Write-Output '    (none)'
} else {
    foreach ($f in $found) { Write-Output ('    ' + $f.Version + '  ' + $f.Hive + '  ' + $f.Code) }
}
if ($found.Count -gt 1) {
    Write-Output ('    [WARN] ' + $found.Count + ' entries registered - an old one was not removed')
}
