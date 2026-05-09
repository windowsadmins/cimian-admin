# CimianAdmin postinstall — register Start Menu shortcut.
# cimipkg auto-injects: $installLocation, $payloadRoot, $payloadDir
$ErrorActionPreference = 'Stop'

if (-not $installLocation) { $installLocation = 'C:\Program Files\CimianAdmin' }
$exe = Join-Path $installLocation 'CimianAdmin.exe'

if (-not (Test-Path $exe)) {
    Write-Host "[CimianAdmin] ERROR: Binary not found at $exe" -ForegroundColor Red
    exit 1
}

$startMenuDir = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs'
$shortcut = Join-Path $startMenuDir 'CimianAdmin.lnk'

$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcut)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $installLocation
$lnk.Description = 'Cimian administrator dashboard'
$lnk.Save()

Write-Host "[CimianAdmin] Installed shortcut: $shortcut" -ForegroundColor Green
exit 0
