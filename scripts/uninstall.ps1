# CimianStudio uninstall
#
# Fires only on standalone uninstall — cimipkg conditions this CA with
# `(REMOVE="ALL") AND NOT UPGRADINGPRODUCTCODE`, so it does NOT run during
# the previous-version removal pass of a major upgrade (postinstall.ps1 will
# re-create whatever this tears down).
$ErrorActionPreference = 'Continue'

Write-Host "CimianStudio uninstall: phase=$($env:CIMIAN_PHASE) version=$($env:CIMIAN_VERSION)" -ForegroundColor Yellow

# 1. Terminate any running CimianStudio.exe so RemoveFiles isn't blocked.
try {
    $procs = Get-Process -Name 'CimianStudio' -ErrorAction SilentlyContinue
    if ($procs) {
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        try { & taskkill /F /IM CimianStudio.exe /T 2>$null } catch { }
    }
} catch {
    Write-Warning "Process termination had non-fatal errors: $_"
}

# 2. Remove our Start Menu shortcut only — leave the shared "Cimian" folder
# in place because CimianTools (Managed Software Center) may still own it.
# If we were the last package in the folder, prune the folder too so the
# Start Menu doesn't show an empty Cimian group.
try {
    $startMenuPath = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Cimian'
    $shortcutPath = Join-Path $startMenuPath 'CimianStudio.lnk'
    if (Test-Path $shortcutPath) {
        Remove-Item -Path $shortcutPath -Force -ErrorAction Stop
        Write-Host "Removed Start Menu shortcut: $shortcutPath"
    }
    if ((Test-Path $startMenuPath) -and -not (Get-ChildItem -Path $startMenuPath -Force -ErrorAction SilentlyContinue)) {
        Remove-Item -Path $startMenuPath -Force -ErrorAction Stop
        Write-Host "Removed empty Cimian Start Menu folder"
    }
} catch {
    Write-Warning "Failed to remove Start Menu shortcut: $_"
}

# 3. Remove HKLM\SOFTWARE\Cimian\CimianStudio registry stamp. The parent
# HKLM\SOFTWARE\Cimian key is owned by CimianTools — leave it alone.
try {
    $registryPath = 'HKLM:\SOFTWARE\Cimian\CimianStudio'
    if (Test-Path $registryPath) {
        Remove-Item -Path $registryPath -Recurse -Force -ErrorAction Stop
        Write-Host "Removed $registryPath"
    }
} catch {
    Write-Warning "Failed to remove registry key: $_"
}

Write-Host 'CimianStudio uninstall completed' -ForegroundColor Yellow
exit 0
