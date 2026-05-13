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

.PARAMETER Release
    Run the full release pipeline locally: `dotnet publish` per arch (x64 +
    arm64 by default), sign binaries, run cimipkg to produce MSI + nupkg,
    sign the MSI, and drop everything in ./release/. CI ships unsigned
    artifacts because hosted runners can't access the enterprise cert — this
    mode is how we produce the signed install media for actual deployment.

.PARAMETER Upload
    After `-Release`, push the signed artifacts to a GitHub release tag,
    replacing any unsigned files of the same name (gh release upload
    --clobber). Requires the gh CLI and contents:write on the repo.

.PARAMETER Version
    Version string baked into the build and used in artifact filenames.
    Defaults to yyyy.M.d. Must be a valid System.Version (no extra tokens).

.PARAMETER Install
    After `-Release`, msiexec /i the freshly-signed MSI for the current
    architecture. Triggers a UAC prompt. Useful for fast install-and-test
    loops; do NOT use against a production host.

.EXAMPLE
    .\build.ps1
    Build + sign for Debug/x64 (dev iteration).

.EXAMPLE
    .\build.ps1 -Run
    Build + sign + launch.

.EXAMPLE
    .\build.ps1 -Release
    Full local release pipeline: publish + sign binaries + cimipkg + sign MSI
    for x64 and arm64. Outputs to ./release/.

.EXAMPLE
    .\build.ps1 -Release -Architecture x64 -Install
    Build a signed x64 MSI and install it on this machine for testing.

.EXAMPLE
    .\build.ps1 -Release -Upload v0.3.0
    Build signed artifacts and push them to the v0.3.0 GitHub release,
    replacing the unsigned ones CI uploaded.

.EXAMPLE
    .\build.ps1 -Configuration Release -Architecture arm64 -Clean
    Clean Release ARM64 dev build with signing (no packaging).

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

    [switch]$Run,

    [switch]$Release,

    [string]$Upload,

    [string]$Version,

    [switch]$Install
)

$ErrorActionPreference = 'Stop'

# Capture which params the caller actually passed before any function rewrites
# its own $PSBoundParameters. Used by Invoke-Release to decide whether
# -Architecture means "this arch only" (explicit) or "fall back to defaults".
$script:ScriptBoundParameters = $PSBoundParameters

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

# --- Release pipeline (publish + sign + cimipkg + sign MSI) ----------------

function Resolve-ReleaseVersion {
    if ($Version) {
        # Validate so cimipkg / MSI Property[ProductVersion] don't choke on a
        # malformed string later. yyyy.M.d.HHmm and yyyy.M.d both parse.
        try { [void][System.Version]::Parse($Version) }
        catch { throw "Invalid -Version '$Version' — must be a valid System.Version (e.g. 2026.5.13 or 1.2.3)." }
        return $Version
    }
    if ($env:CIMIAN_VERSION) { return $env:CIMIAN_VERSION }
    return Get-Date -Format 'yyyy.M.d'
}

function Get-CimipkgPath {
    # Try sibling CimianTools repo first (devs working on both have it built).
    $sibling = Join-Path $repoRoot '..\CimianTools\release\x64\cimipkg.exe'
    if (Test-Path $sibling) { return (Resolve-Path $sibling).Path }

    $sibling = Join-Path $repoRoot '..\CimianTools\release\arm64\cimipkg.exe'
    if (Test-Path $sibling) { return (Resolve-Path $sibling).Path }

    # Then PATH.
    $onPath = Get-Command cimipkg.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    # Last resort: pull the latest release of windowsadmins/cimian-pkg.
    $toolsDir = Join-Path $repoRoot '.tools'
    $local = Join-Path $toolsDir 'cimipkg.exe'
    if (Test-Path $local) { return $local }

    Write-BuildLog 'cimipkg.exe not found locally; downloading from windowsadmins/cimian-pkg release'
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'cimipkg.exe not found and gh CLI is unavailable. Install cimipkg or gh and retry.'
    }
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
    # Prefer zipped distribution if present, else direct exe.
    & gh release download --repo windowsadmins/cimian-pkg --pattern 'cimipkg-win-x64.zip' --dir $toolsDir 2>$null
    $zip = Join-Path $toolsDir 'cimipkg-win-x64.zip'
    if (Test-Path $zip) {
        Expand-Archive -Path $zip -DestinationPath $toolsDir -Force
        Remove-Item $zip -Force -ErrorAction SilentlyContinue
    } else {
        & gh release download --repo windowsadmins/cimian-pkg --pattern 'cimipkg.exe' --dir $toolsDir
    }
    if (-not (Test-Path $local)) {
        throw 'Failed to download cimipkg.exe'
    }
    return $local
}

function Invoke-ReleaseForArch {
    param(
        [Parameter(Mandatory)][string]$Arch,
        [Parameter(Mandatory)][string]$ReleaseVersion,
        [Parameter(Mandatory)][string]$CimipkgExe,
        [string]$SignThumbprint,
        [string]$SignStore = 'CurrentUser'
    )

    $rid = "win-$Arch"
    $publishDir = Join-Path $repoRoot "publish\$Arch"
    $stagingDir = Join-Path $repoRoot "staging-$Arch"
    $releaseDir = Join-Path $repoRoot 'release'

    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    New-Item -ItemType Directory -Path "$stagingDir\payload" -Force | Out-Null
    New-Item -ItemType Directory -Path "$stagingDir\scripts" -Force | Out-Null

    # 1. dotnet publish — self-contained so the MSI doesn't depend on a
    # specific runtime install order on the target.
    Write-BuildLog "Publishing $Arch ($rid)..."
    & dotnet publish $csproj `
        -c Release -r $rid `
        -p:Platform=$($Arch.ToUpper()) `
        -p:Version=$ReleaseVersion `
        -p:WindowsAppSDKSelfContained=true `
        -p:SelfContained=true `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Arch" }

    # 2. Sign all CimianAdmin-shipped binaries in the publish tree. We
    # deliberately leave Microsoft.* / Windows App SDK runtime DLLs alone —
    # those are already MS-signed and re-signing them invalidates that.
    if ($SignThumbprint) {
        $bins = Get-ChildItem -Path $publishDir -Include 'CimianAdmin.exe', 'CimianAdmin*.dll', 'Cimian.*.dll', 'CimianAdmin.Core.dll', 'CimianAdmin.Infrastructure.dll', 'CimianAdmin.Shared.dll' -Recurse -File |
            Select-Object -ExpandProperty FullName
        if ($bins.Count -gt 0) {
            Invoke-SignArtifacts -Paths $bins -Thumbprint $SignThumbprint -Store $SignStore
        } else {
            Write-BuildLog "No CimianAdmin binaries matched the sign filter — check publish output." 'WARNING'
        }
    }

    # 3. Stage payload + build-info + scripts for cimipkg.
    Copy-Item "$publishDir\*" "$stagingDir\payload\" -Recurse -Force
    (Get-Content (Join-Path $repoRoot 'build-info.yaml') -Raw) `
        -replace '\$\{TIMESTAMP\}', $ReleaseVersion `
        -replace '\$\{ARCH\}', $Arch `
        -replace '(?m)^\s*signing_certificate:.*\r?\n', '' |
        Set-Content "$stagingDir\build-info.yaml" -Encoding UTF8
    Copy-Item (Join-Path $repoRoot 'scripts\*.ps1') "$stagingDir\scripts\" -Force

    # 4. cimipkg → MSI.
    Write-BuildLog "Running cimipkg for $Arch MSI..."
    & $CimipkgExe --verbose $stagingDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "cimipkg MSI build failed for $Arch" }
    $msi = Get-ChildItem "$stagingDir\build\*.msi" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $msi) { throw "MSI output not found for $Arch" }
    $msiDest = Join-Path $releaseDir "CimianAdmin-$Arch-$ReleaseVersion.msi"
    Move-Item $msi.FullName $msiDest -Force

    # 5. Sign the MSI itself so SmartScreen / GPO trust it as a unit.
    if ($SignThumbprint) {
        Invoke-SignArtifacts -Paths @($msiDest) -Thumbprint $SignThumbprint -Store $SignStore
    }

    # 6. cimipkg → nupkg (Chocolatey-compatible). nupkgs aren't traditionally
    # Authenticode-signed; nuget sign needs a different cert chain so we leave
    # them as-is.
    Write-BuildLog "Running cimipkg for $Arch nupkg..."
    & $CimipkgExe --nupkg --verbose $stagingDir | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-BuildLog "nupkg build failed for $Arch (non-fatal)" 'WARNING'
    } else {
        $nupkg = Get-ChildItem "$stagingDir\build\*.nupkg" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($nupkg) {
            Move-Item $nupkg.FullName (Join-Path $releaseDir "CimianAdmin-$Arch-$ReleaseVersion.nupkg") -Force
        }
    }

    # 7. Raw publish zip — mirrors what the CI release.yml uploads.
    Compress-Archive -Path "$publishDir\*" -DestinationPath (Join-Path $releaseDir "CimianAdmin-$Arch.zip") -Force

    Write-BuildLog "Done with $Arch." 'SUCCESS'
    return $msiDest
}

function Invoke-Release {
    $archs = if ($script:ScriptBoundParameters.ContainsKey('Architecture')) {
        @($Architecture)
    } else {
        # Release default ships both arches even though dev defaults to x64.
        @('x64', 'arm64')
    }

    $releaseVersion = Resolve-ReleaseVersion
    Write-BuildLog "Release version: $releaseVersion"

    $cimipkg = Get-CimipkgPath
    Write-BuildLog "Using cimipkg: $cimipkg"

    $thumbprint = $null
    $store = 'CurrentUser'
    if ($shouldSign) {
        if ($Thumbprint) {
            $thumbprint = $Thumbprint
        } else {
            $certInfo = Get-SigningCertThumbprint
            if (-not $certInfo) {
                throw "No signing certificate found whose subject matches '$Global:EnterpriseCertSubject'. Pass -Thumbprint, set CIMIAN_CERT_SUBJECT, or use -NoSign."
            }
            $thumbprint = $certInfo.Thumbprint
            $store = $certInfo.Store
        }
        Write-BuildLog "Signing with cert $thumbprint ($store)"
    } else {
        Write-BuildLog '-NoSign: producing unsigned MSI + binaries' 'WARNING'
    }

    $releaseDir = Join-Path $repoRoot 'release'
    if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

    $msiForCurrentArch = $null
    foreach ($arch in $archs) {
        $msi = Invoke-ReleaseForArch -Arch $arch -ReleaseVersion $releaseVersion -CimipkgExe $cimipkg -SignThumbprint $thumbprint -SignStore $store
        if ($arch -eq $env:PROCESSOR_ARCHITECTURE.ToLower() -or
            ($env:PROCESSOR_ARCHITECTURE -eq 'AMD64' -and $arch -eq 'x64') -or
            ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -and $arch -eq 'arm64')) {
            $msiForCurrentArch = $msi
        }
    }

    Write-BuildLog "Release artifacts in $releaseDir" 'SUCCESS'
    Get-ChildItem $releaseDir | ForEach-Object { Write-BuildLog "  $($_.Name) ($([Math]::Round($_.Length / 1MB, 2)) MB)" }

    if ($Install) {
        if (-not $msiForCurrentArch) {
            Write-BuildLog "-Install requested but no MSI was built for $env:PROCESSOR_ARCHITECTURE — skipping." 'WARNING'
        } else {
            Write-BuildLog "Installing $msiForCurrentArch (UAC expected)..."
            Start-Process msiexec.exe -ArgumentList @('/i', "`"$msiForCurrentArch`"", '/passive', '/norestart') -Verb RunAs -Wait
            Write-BuildLog 'msiexec returned.' 'SUCCESS'
        }
    }

    if ($Upload) {
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
            throw '-Upload specified but gh CLI is not on PATH.'
        }
        Write-BuildLog "Uploading signed artifacts to release $Upload (replacing existing)..."
        $files = Get-ChildItem -Path $releaseDir -File | ForEach-Object { $_.FullName }
        # --clobber so we overwrite the unsigned versions CI uploaded.
        & gh release upload $Upload --clobber @files
        if ($LASTEXITCODE -ne 0) { throw "gh release upload failed (exit $LASTEXITCODE)" }
        Write-BuildLog "Upload complete." 'SUCCESS'
    }
}

# --- Main ------------------------------------------------------------------

try {
    if ($Release) {
        Invoke-Release
        Write-BuildLog 'Done.' 'SUCCESS'
        return
    }

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
