# CimianStudio preinstall
#
# Fires only on upgrade/reinstall — cimipkg conditions this CA with
# `PREVIOUSVERSIONSINSTALLED OR (Installed AND NOT (REMOVE="ALL"))`. We use it
# to stop a running CimianStudio.exe so the MSI's RemoveFiles pass can replace
# the binary without WindowsAppSDK file locks. Fresh installs short-circuit
# below — there's nothing to tear down.
$ErrorActionPreference = 'Continue'

Write-Host "CimianStudio preinstall: phase=$($env:CIMIAN_PHASE) version=$($env:CIMIAN_VERSION)" -ForegroundColor Green

if ($env:CIMIAN_PHASE -eq 'fresh') {
    Write-Host 'Fresh install detected — nothing to tear down, exiting.'
    exit 0
}

try {
    $procs = Get-Process -Name 'CimianStudio' -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "Stopping $($procs.Count) CimianStudio process(es) for upgrade..."
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        # Sometimes WinUI's Process Lifetime Manager leaves a child; taskkill /T
        # cleans up the whole tree.
        try { & taskkill /F /IM CimianStudio.exe /T 2>$null } catch { }
    }
} catch {
    Write-Warning "Process termination had non-fatal errors: $_"
}

Write-Host 'CimianStudio preinstall completed' -ForegroundColor Green
exit 0
