# CimianAdmin postinstall
#
# Fires on fresh install and upgrade/reinstall — cimipkg conditions this CA
# with `NOT (REMOVE="ALL")`. Must be idempotent: the same operations may run
# after a fresh install OR after an upgrade replaces the binaries in place.
# Mirrors the pattern used by CimianTools (Managed Software Center) so both
# packages register under one Start Menu folder named "Cimian".
$ErrorActionPreference = 'Continue'

$InstallDir = 'C:\Program Files\CimianAdmin'
$arch = $env:PROCESSOR_ARCHITECTURE
$phase = $env:CIMIAN_PHASE
$version = $env:CIMIAN_VERSION

Write-Host "CimianAdmin postinstall: phase=$phase version=$version arch=$arch" -ForegroundColor Green

if (-not (Test-Path $InstallDir)) {
    Write-Host "ERROR: Installation directory not found: $InstallDir" -ForegroundColor Red
    exit 1
}

$exe = Join-Path $InstallDir 'CimianAdmin.exe'
if (-not (Test-Path $exe)) {
    Write-Host "ERROR: CimianAdmin.exe not found at $exe" -ForegroundColor Red
    exit 1
}

# Start Menu shortcut. The "Cimian" folder is shared with CimianTools'
# Managed Software Center entry so a deployment that ships both lands them
# grouped under a single Start Menu group, mirroring how Munki ships Munki +
# Managed Software Center on macOS.
try {
    $startMenuPath = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Cimian'
    if (-not (Test-Path $startMenuPath)) {
        New-Item -ItemType Directory -Path $startMenuPath -Force | Out-Null
    }
    $shortcutPath = Join-Path $startMenuPath 'CimianAdmin.lnk'
    $wshell = New-Object -ComObject WScript.Shell
    $shortcut = $wshell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = 'Cimian administrator dashboard — author packages, manifests, and catalogs'
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Save()
    Write-Host "Installed Start Menu shortcut: $shortcutPath"
} catch {
    Write-Warning "Failed to create Start Menu shortcut: $_"
}

# Registry stamp under HKLM\SOFTWARE\Cimian\CimianAdmin so inventory tooling
# can detect the installed version. Uses a sub-key off the shared Cimian
# root rather than a parallel key, so a single inventory query covers both
# CimianTools and CimianAdmin.
try {
    if ([string]::IsNullOrEmpty($version)) {
        $versionInfo = (Get-ItemProperty $exe -ErrorAction SilentlyContinue).VersionInfo
        if ($versionInfo) {
            $version = $versionInfo.ProductVersion
            if ([string]::IsNullOrEmpty($version)) { $version = $versionInfo.FileVersion }
        }
    }
    $registryPath = 'HKLM:\SOFTWARE\Cimian\CimianAdmin'
    if (-not (Test-Path $registryPath)) { New-Item -Path $registryPath -Force | Out-Null }
    Set-ItemProperty -Path $registryPath -Name 'Version' -Value ($version ?? 'unknown') -Type String
    Set-ItemProperty -Path $registryPath -Name 'InstallPath' -Value $InstallDir -Type String
    Write-Host "Wrote registry stamp at $registryPath"
} catch {
    Write-Warning "Failed to write registry stamp: $_"
}

Write-Host "CimianAdmin postinstall completed ($phase)" -ForegroundColor Green
exit 0
