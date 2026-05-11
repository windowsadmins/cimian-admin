#Requires -Version 7.0
<#
.SYNOPSIS
    Builds, signs, and (optionally) launches CimianAdmin.

.DESCRIPTION
    Mirrors the structure of `packages/CimianTools/build.ps1` but scoped to this single
    WinUI 3 app. Looks up the enterprise code-signing certificate by subject (defaults to
    "EmilyCarrU", overridable via $env:CIMIAN_CERT_SUBJECT), runs `dotnet build` against
    the WinUI csproj, signs the produced CimianAdmin.exe with signtool + a trusted RFC3161
    timestamp, and optionally launches the freshly-signed binary.

    Defender Exploit Guard's ASR rule "Block executable files from running unless they
    meet a prevalence, age, or trusted list criterion" (01443614-cd74-433a-b99e-2ecdc07bfc25)
    refuses to start unsigned freshly-built exes. Signing satisfies the trusted-list
    criterion, so this script is the supported way to launch local dev builds.

.PARAMETER Sign
    Sign the produced CimianAdmin.exe (default).

.PARAMETER NoSign
    Skip signing. The binary will be blocked by ASR on this machine — use only when
    targeting a different host or when you've added a Defender exclusion yourself.

.PARAMETER Thumbprint
    Override certificate selection. If omitted, the cert with subject containing
    $Global:EnterpriseCertSubject is used.

.PARAMETER Configuration
    Debug | Release. Default: Debug.

.PARAMETER Architecture
    x64 | arm64. Default: x64.

.PARAMETER Clean
    Delete bin/obj before building.

.PARAMETER Run
    Launch CimianAdmin.exe after a successful (signed) build.

.EXAMPLE
    .\build.ps1
    Build + sign for Debug/x64.

.EXAMPLE
    .\build.ps1 -Run
    Build + sign + launch.

.EXAMPLE
    .\build.ps1 -Configuration Release -Architecture arm64 -Clean
    Clean Release ARM64 build with signing.

.EXAMPLE
    $env:CIMIAN_CERT_SUBJECT = 'YourOrgName'; .\build.ps1
    Use a different certificate subject for one build.
#>

[CmdletBinding(DefaultParameterSetName = 'Default')]
param(
    [Parameter(ParameterSetName = 'Default')]
    [switch]$Sign,

    [Parameter(ParameterSetName = 'NoSign')]
    [switch]$NoSign,

    [string]$Thumbprint,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    [switch]$Clean,

    [switch]$Run
)

$ErrorActionPreference = 'Stop'

# --- Configuration ---------------------------------------------------------

$Global:EnterpriseCertSubject = $env:CIMIAN_CERT_SUBJECT ?? 'EmilyCarrU'

$repoRoot = $PSScriptRoot
$csproj = Join-Path $repoRoot 'src\CimianAdmin\CimianAdmin.csproj'
$rid = if ($Architecture -eq 'arm64') { 'win-arm64' } else { 'win-x64' }
$tfm = 'net10.0-windows10.0.19041.0'
$binDir = Join-Path $repoRoot "src\CimianAdmin\bin\$Configuration\$tfm\$rid"
$exePath = Join-Path $binDir 'CimianAdmin.exe'

# Sign by default unless -NoSign was passed.
$shouldSign = -not $NoSign

# --- Logging ---------------------------------------------------------------

function Write-BuildLog {
    param([string]$Message, [string]$Level = 'INFO')
    $color = switch ($Level) {
        'SUCCESS' { 'Green' }
        'WARNING' { 'Yellow' }
        'ERROR'   { 'Red' }
        default   { 'Cyan' }
    }
    $prefix = "[{0,-7}]" -f $Level
    Write-Host "$prefix $Message" -ForegroundColor $color
}

# --- Cert lookup (mirrors CimianTools) -------------------------------------

function Get-SigningCertThumbprint {
    [OutputType([hashtable])]
    param()

    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
        $_.HasPrivateKey -and $_.Subject -like "*$Global:EnterpriseCertSubject*"
    } | Sort-Object NotAfter -Descending | Select-Object -First 1

    if ($cert) {
        return @{ Thumbprint = $cert.Thumbprint; Store = 'CurrentUser' }
    }

    $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object {
        $_.HasPrivateKey -and $_.Subject -like "*$Global:EnterpriseCertSubject*"
    } | Sort-Object NotAfter -Descending | Select-Object -First 1

    if ($cert) {
        return @{ Thumbprint = $cert.Thumbprint; Store = 'LocalMachine' }
    }

    return $null
}

# --- signtool.exe discovery (mirrors CimianTools) --------------------------

$Global:SignToolPath = $null

function Get-SignToolPath {
    if ($Global:SignToolPath -and (Test-Path $Global:SignToolPath)) {
        return $Global:SignToolPath
    }

    $c = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($c -and $c.Source -match '\\x64\\') {
        $Global:SignToolPath = $c.Source
        return $Global:SignToolPath
    }

    $programFilesx86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $searchRoot = Join-Path $programFilesx86 'Windows Kits\10\bin'
    if (Test-Path $searchRoot) {
        $candidates = Get-ChildItem -Path $searchRoot -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object { $_.Directory.Parent.Name } -Descending
        if ($candidates -and $candidates.Count -gt 0) {
            $Global:SignToolPath = $candidates[0].FullName
            return $Global:SignToolPath
        }
    }

    try {
        $kitsRoot = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -Name KitsRoot10 -ErrorAction SilentlyContinue
        if ($kitsRoot) {
            $regRoot = Join-Path $kitsRoot.KitsRoot10 'bin'
            if (Test-Path $regRoot) {
                $candidates = Get-ChildItem -Path $regRoot -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
                    Where-Object { $_.FullName -match '\\x64\\' } |
                    Sort-Object { $_.Directory.Parent.Name } -Descending
                if ($candidates -and $candidates.Count -gt 0) {
                    $Global:SignToolPath = $candidates[0].FullName
                    return $Global:SignToolPath
                }
            }
        }
    } catch {}

    return $null
}

# --- Signing (mirrors CimianTools — retries across RFC3161 TSAs) -----------

function Invoke-SignArtifacts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Paths,
        [Parameter(Mandatory)][string]$Thumbprint,
        [string]$Store = 'CurrentUser',
        [int]$MaxAttempts = 2
    )

    $existing = @($Paths | Where-Object { Test-Path -LiteralPath $_ })
    if ($existing.Count -eq 0) {
        throw "None of the supplied files exist: $($Paths -join ', ')"
    }

    $signToolExe = Get-SignToolPath
    if (-not $signToolExe) {
        throw 'signtool.exe not found. Install Windows 10/11 SDK (Signing Tools).'
    }

    $storeParam = if ($Store -eq 'CurrentUser') { @('/s', 'My') } else { @('/s', 'My', '/sm') }
    $tsas = @(
        'http://timestamp.digicert.com',
        'http://timestamp.sectigo.com',
        'http://timestamp.entrust.net/TSS/RFC3161sha2TS'
    )

    # Try in-process signing first (no UAC) — fast-fails on ASR-locked machines.
    $attempt = 0
    while ($attempt -lt $MaxAttempts) {
        $attempt++
        foreach ($tsa in $tsas) {
            $signArgs = @('sign') + $storeParam + @(
                '/sha1', $Thumbprint,
                '/fd', 'SHA256',
                '/td', 'SHA256',
                '/tr', $tsa,
                '/v'
            ) + $existing

            Write-BuildLog "signtool (attempt $attempt, $tsa) on $($existing.Count) file(s)"
            & $signToolExe @signArgs 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-BuildLog "Signed: $($existing -join ', ')" 'SUCCESS'
                return
            }
        }
    }

    # Fallback: sudo signtool. ONE elevated invocation that signs every file —
    # signtool accepts multiple file arguments, so this is one UAC prompt total,
    # not one per file.
    $sudoCmd = Get-Command sudo -ErrorAction SilentlyContinue
    if (-not $sudoCmd) {
        throw "Signing failed and sudo is not available to retry elevated. Last in-process exit code: $LASTEXITCODE"
    }

    Write-BuildLog "In-process signing was denied; retrying with sudo (UAC prompt expected)" 'WARNING'

    $sudoArgs = @(
        $signToolExe,
        'sign'
    ) + $storeParam + @(
        '/sha1', $Thumbprint,
        '/fd', 'SHA256',
        '/td', 'SHA256',
        '/tr', $tsas[0],
        '/v'
    ) + $existing

    & sudo @sudoArgs
    if ($LASTEXITCODE -ne 0) {
        throw "sudo signtool failed (exit $LASTEXITCODE)"
    }
    Write-BuildLog "Signed (elevated): $($existing.Count) file(s)" 'SUCCESS'
}

function Invoke-SignArtifact {
    # Kept as a thin shim for any callers that still expect single-file signing.
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Thumbprint,
        [string]$Store = 'CurrentUser',
        [int]$MaxAttempts = 2
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "File not found: $Path"
    }

    $signToolExe = Get-SignToolPath
    if (-not $signToolExe) {
        throw 'signtool.exe not found. Install Windows 10/11 SDK (Signing Tools).'
    }

    $storeParam = if ($Store -eq 'CurrentUser') { @('/s', 'My') } else { @('/s', 'My', '/sm') }

    $tsas = @(
        'http://timestamp.digicert.com',
        'http://timestamp.sectigo.com',
        'http://timestamp.entrust.net/TSS/RFC3161sha2TS'
    )

    # First: try in-process signing (no UAC). On enterprise-locked machines whose
    # ASR rules deny non-elevated processes from modifying freshly-built binaries,
    # this loops and fails fast so we get to the sudo fallback quickly.
    $attempt = 0
    while ($attempt -lt $MaxAttempts) {
        $attempt++
        foreach ($tsa in $tsas) {
            $signArgs = @('sign') + $storeParam + @(
                '/sha1', $Thumbprint,
                '/fd', 'SHA256',
                '/td', 'SHA256',
                '/tr', $tsa,
                '/v',
                $Path
            )

            Write-BuildLog "signtool (attempt $attempt, $tsa): $([System.IO.Path]::GetFileName($Path))"
            & $signToolExe @signArgs 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-BuildLog "Signed: $Path" 'SUCCESS'
                return
            }
        }
    }

    # Fallback: sudo signtool. On Windows 11 the built-in sudo gives one UAC
    # prompt and runs signtool elevated. This is the path the bootstrap build
    # uses for the same enterprise certificate scenario.
    $sudoCmd = Get-Command sudo -ErrorAction SilentlyContinue
    if (-not $sudoCmd) {
        throw "Signing failed and sudo is not available to retry elevated. Last in-process exit code: $LASTEXITCODE"
    }

    Write-BuildLog "In-process signing was denied; retrying with sudo (UAC prompt expected)" 'WARNING'

    $sudoArgs = @(
        $signToolExe,
        'sign'
    ) + $storeParam + @(
        '/sha1', $Thumbprint,
        '/fd', 'SHA256',
        '/td', 'SHA256',
        '/tr', $tsas[0],
        '/v',
        $Path
    )

    & sudo @sudoArgs
    if ($LASTEXITCODE -ne 0) {
        throw "sudo signtool failed (exit $LASTEXITCODE) for $Path"
    }
    Write-BuildLog "Signed (elevated): $Path" 'SUCCESS'
}

# --- Build steps -----------------------------------------------------------

function Invoke-Clean {
    Write-BuildLog 'Cleaning bin/obj…'
    Get-ChildItem -Path (Join-Path $repoRoot 'src') -Recurse -Directory -Include 'bin', 'obj' -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path (Join-Path $repoRoot 'tests') -Recurse -Directory -Include 'bin', 'obj' -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Write-BuildLog 'Clean complete.' 'SUCCESS'
}

function Stop-RunningApp {
    Get-Process -Name CimianAdmin -ErrorAction SilentlyContinue | ForEach-Object {
        Write-BuildLog "Stopping CimianAdmin PID $($_.Id) so the build can replace its files"
        $_ | Stop-Process -Force
    }
}

function Invoke-Build {
    Write-BuildLog "Building $Configuration/$rid…"
    $args = @(
        'build', $csproj,
        '-c', $Configuration,
        '-r', $rid,
        '--self-contained', 'false',
        '-nologo'
    )
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed (exit $LASTEXITCODE)"
    }
    if (-not (Test-Path $exePath)) {
        throw "Build succeeded but $exePath was not produced — check the output above."
    }
    Write-BuildLog "Built: $exePath" 'SUCCESS'
}

# --- Main ------------------------------------------------------------------

try {
    if ($Clean) { Invoke-Clean }

    Stop-RunningApp
    Invoke-Build

    if ($shouldSign) {
        if (-not $Thumbprint) {
            $certInfo = Get-SigningCertThumbprint
            if (-not $certInfo) {
                throw @"
No signing certificate found whose subject matches '$Global:EnterpriseCertSubject'.
Override with -Thumbprint <thumbprint>, set `$env:CIMIAN_CERT_SUBJECT to a different
subject, or pass -NoSign if you can launch unsigned binaries on this machine.
"@
            }
            $Thumbprint = $certInfo.Thumbprint
            $store = $certInfo.Store
        } else {
            $store = 'CurrentUser'
        }

        # Sign the apphost AND the companion DLLs in one shot — single UAC prompt
        # via sudo, no per-file elevation churn.
        $toSign = @($exePath) + (@(
            'CimianAdmin.dll',
            'CimianAdmin.Core.dll',
            'CimianAdmin.Infrastructure.dll',
            'CimianAdmin.Shared.dll'
        ) | ForEach-Object { Join-Path $binDir $_ } | Where-Object { Test-Path $_ })

        Invoke-SignArtifacts -Paths $toSign -Thumbprint $Thumbprint -Store $store
    } else {
        Write-BuildLog 'Signing skipped (-NoSign). Defender Exploit Guard may block the binary.' 'WARNING'
    }

    if ($Run) {
        Write-BuildLog "Launching $exePath…"
        Start-Process -FilePath $exePath
    }

    Write-BuildLog 'Done.' 'SUCCESS'
}
catch {
    Write-BuildLog $_.Exception.Message 'ERROR'
    exit 1
}
