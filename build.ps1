
<#
.SYNOPSIS
    Builds 'ros2cs'
.DESCRIPTION
    This script runs colcon build
.PARAMETER with_tests
    build with tests
.PARAMETER standalone
    standalone build
.PARAMETER build_base
    Optional colcon build base directory. Can also be set with ROS2CS_BUILD_BASE.
    Defaults to build-$ROS_DISTRO to keep CMake caches isolated per ROS distro.
.PARAMETER install_base
    Optional colcon install base directory. Can also be set with ROS2CS_INSTALL_BASE.
    Defaults to install-$ROS_DISTRO to keep runtime DLLs isolated per ROS distro.
.PARAMETER help
    show help and exit

Modifications Copyright (c) 2026 Jianbin Liu.

Modifications by Jianbin Liu:
- Defaulted Windows builds to Ninja for ROS 2 Jazzy/MSVC stability and speed.
- Added explicit parallel worker selection with ROS2CS_PARALLEL_WORKERS override.
- Added optional compiler launcher support through ROS2CS_COMPILER_LAUNCHER or sccache auto-detection.
- Added optional short colcon build base support through -build_base or ROS2CS_BUILD_BASE.
- Added optional colcon install base support through -install_base or ROS2CS_INSTALL_BASE.
- Added ROS2CS_EVENT_HANDLER override and defaulted colcon output to console_cohesion+.
- Added build evidence metadata and a default install-base distro guard.
- Isolated default build/install bases per ROS_DISTRO to prevent cross-distro DLL shadowing.

Based on upstream RobotecAI ros2cs scripts, Apache-2.0.
#>
Param (
    [Parameter(Mandatory=$false)][switch]$help=$false,
    [Parameter(Mandatory=$false)][switch]$with_tests=$false,
    [Parameter(Mandatory=$false)][switch]$standalone=$false,
    [Parameter(Mandatory=$false)][string]$build_base=$Env:ROS2CS_BUILD_BASE,
    [Parameter(Mandatory=$false)][string]$install_base=$Env:ROS2CS_INSTALL_BASE
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($help) {
    Write-Host "Usage: .\build.ps1 [-with_tests] [-standalone] [-build_base PATH] [-install_base PATH]"
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -with_tests         Build tests."
    Write-Host "  -standalone         Build standalone runtime layout."
    Write-Host "  -build_base PATH    Optional colcon build base directory. Defaults to build-`$ROS_DISTRO."
    Write-Host "  -install_base PATH  Optional colcon install base directory. Defaults to install-`$ROS_DISTRO."
    exit 0
}

function Get-ParallelWorkers {
    # Worker count is a throughput policy; build correctness must not depend on a specific value.
    if (-not [string]::IsNullOrWhiteSpace($Env:ROS2CS_PARALLEL_WORKERS)) {
        $workers = 0
        if ([int]::TryParse($Env:ROS2CS_PARALLEL_WORKERS, [ref]$workers) -and $workers -gt 0) {
            return $workers
        }
        throw "ROS2CS_PARALLEL_WORKERS must be a positive integer."
    }

    return [System.Environment]::ProcessorCount
}

function Get-CompilerLauncher {
    # Compiler launcher use is best-effort acceleration and must not be required for a correct build.
    if (-not [string]::IsNullOrWhiteSpace($Env:ROS2CS_COMPILER_LAUNCHER)) {
        return $Env:ROS2CS_COMPILER_LAUNCHER
    }

    $sccache = Get-Command sccache -ErrorAction SilentlyContinue
    if ($null -ne $sccache) {
        return "sccache"
    }

    return $null
}

function Get-EffectiveInstallBase {
    param([string]$InstallBase)

    $repoRoot = (Get-Location).Path
    if ([string]::IsNullOrWhiteSpace($InstallBase)) {
        return (Join-Path $repoRoot "install-$Env:ROS_DISTRO")
    }

    if ([System.IO.Path]::IsPathRooted($InstallBase)) {
        return $InstallBase
    }

    return (Join-Path $repoRoot $InstallBase)
}

function Get-EffectiveBuildBase {
    param([string]$BuildBase)

    $repoRoot = (Get-Location).Path
    if ([string]::IsNullOrWhiteSpace($BuildBase)) {
        return (Join-Path $repoRoot "build-$Env:ROS_DISTRO")
    }

    if ([System.IO.Path]::IsPathRooted($BuildBase)) {
        return $BuildBase
    }

    return (Join-Path $repoRoot $BuildBase)
}

function Get-GitCommit {
    try {
        $commit = (& git rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($commit)) {
            return $commit.Trim()
        }
    } catch {
        # Build evidence should not make local source builds depend on git availability.
    }

    return "unknown"
}

function Assert-DefaultInstallBaseMatchesDistro {
    param(
        [string]$InstallBase,
        [bool]$ExplicitInstallBase
    )

    if ($ExplicitInstallBase) {
        return
    }

    $markerPath = Join-Path $InstallBase ".ros2cs_build_distro"
    if (-not (Test-Path -LiteralPath $markerPath)) {
        return
    }

    $recordedDistro = (Get-Content -LiteralPath $markerPath -Raw).Trim()
    if (-not [string]::IsNullOrWhiteSpace($recordedDistro) -and $recordedDistro -ne $Env:ROS_DISTRO) {
        throw "Default install base '$InstallBase' was last built for ROS_DISTRO='$recordedDistro', but current ROS_DISTRO='$Env:ROS_DISTRO'. Pass -install_base for an isolated build or remove the default install directory intentionally."
    }
}

function Write-BuildEvidence {
    param(
        [string]$InstallBase,
        [string]$Standalone,
        [string]$BuildTesting
    )

    New-Item -ItemType Directory -Path $InstallBase -Force | Out-Null

    $markerPath = Join-Path $InstallBase ".ros2cs_build_distro"
    Set-Content -LiteralPath $markerPath -Value $Env:ROS_DISTRO -Encoding ASCII

    $infoPath = Join-Path $InstallBase "ros2cs_build_info.txt"
    $lines = @(
        "ros2cs_commit=$(Get-GitCommit)",
        "ros_distro=$Env:ROS_DISTRO",
        "build_timestamp_utc=$([DateTimeOffset]::UtcNow.ToString('o'))",
        "repo_path=$((Get-Location).Path)",
        "install_base=$InstallBase",
        "standalone=$Standalone",
        "build_testing=$BuildTesting"
    )
    Set-Content -LiteralPath $infoPath -Value $lines -Encoding UTF8
}

if ([string]::IsNullOrEmpty($Env:ROS_DISTRO)) {
    Write-Host "Source your ros2 distro first (foxy, galactic, humble, jazzy, lyrical or rolling are supported)" -ForegroundColor Red
    exit 1
}

# Validate to prevent path injection; ROS_DISTRO is interpolated into marker and repos filenames.
if ($Env:ROS_DISTRO -notmatch '^[a-z][a-z0-9_]*$') {
    Write-Host "Invalid ROS_DISTRO value: '$Env:ROS_DISTRO'." -ForegroundColor Red
    exit 1
}

$msg="Build started."
$tests_switch="OFF"
if($with_tests) {
    $msg+=" (with tests)"
    $tests_switch="ON"
}
$standalone_switch="OFF"
if($standalone) {
    $msg+=" (standalone)"
    $standalone_switch="ON"
}

$parallelWorkers = Get-ParallelWorkers
$compilerLauncher = Get-CompilerLauncher
$explicitInstallBase = -not [string]::IsNullOrWhiteSpace($install_base)
$effectiveBuildBase = Get-EffectiveBuildBase -BuildBase $build_base
$effectiveInstallBase = Get-EffectiveInstallBase -InstallBase $install_base
Assert-DefaultInstallBaseMatchesDistro -InstallBase $effectiveInstallBase -ExplicitInstallBase $explicitInstallBase
# console_cohesion+ buffers output per package for readable local logs; CI can override to console_direct+.
$eventHandler = if ([string]::IsNullOrWhiteSpace($Env:ROS2CS_EVENT_HANDLER)) {
    "console_cohesion+"
} else {
    $Env:ROS2CS_EVENT_HANDLER
}
$cmakeArgs = @(
    "-G", "Ninja",
    "-DSTANDALONE_BUILD=$standalone_switch",
    "-DCMAKE_BUILD_TYPE=Release",
    "-DBUILD_TESTING=$tests_switch"
)

if (-not [string]::IsNullOrWhiteSpace($Env:COLCON_PYTHON_EXECUTABLE)) {
    $cmakeArgs += "-DPython3_EXECUTABLE:FILEPATH=$Env:COLCON_PYTHON_EXECUTABLE"
}

if (-not [string]::IsNullOrWhiteSpace($compilerLauncher)) {
    $cmakeArgs += "-DCMAKE_C_COMPILER_LAUNCHER=$compilerLauncher"
    $cmakeArgs += "-DCMAKE_CXX_COMPILER_LAUNCHER=$compilerLauncher"
    $msg += " (compiler launcher: $compilerLauncher)"
}

$colconArgs = @("build", "--build-base", "$effectiveBuildBase", "--install-base", "$effectiveInstallBase")
$msg += " (build base: $effectiveBuildBase)"
$msg += " (install base: $effectiveInstallBase)"

$colconArgs += @(
    "--parallel-workers", "$parallelWorkers",
    # Merge install produces one install tree, which matches downstream setup.* sourcing and R2FU packaging assumptions.
    "--merge-install",
    "--event-handlers", "$eventHandler",
    "--cmake-args"
) + $cmakeArgs

$msg += " (workers: $parallelWorkers, generator: Ninja, event handler: $eventHandler)"

Write-Host $msg -ForegroundColor Green
colcon @colconArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-BuildEvidence -InstallBase $effectiveInstallBase -Standalone $standalone_switch -BuildTesting $tests_switch
