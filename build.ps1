
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

Modifications Copyright (c) 2026 Jianbin Liu.

Modifications by Jianbin Liu:
- Defaulted Windows builds to Ninja for ROS 2 Jazzy/MSVC stability and speed.
- Added explicit parallel worker selection with ROS2CS_PARALLEL_WORKERS override.
- Added optional compiler launcher support through ROS2CS_COMPILER_LAUNCHER or sccache auto-detection.
- Added optional short colcon build base support through -build_base or ROS2CS_BUILD_BASE.
#>
Param (
    [Parameter(Mandatory=$false)][switch]$with_tests=$false,
    [Parameter(Mandatory=$false)][switch]$standalone=$false,
    [Parameter(Mandatory=$false)][string]$build_base=$Env:ROS2CS_BUILD_BASE
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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

if ([string]::IsNullOrEmpty($Env:ROS_DISTRO)) {
    Write-Host "Source your ros2 distro first (foxy, galactic, humble, jazzy or rolling are supported)" -ForegroundColor Red
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
$cmakeArgs = @(
    "-G", "Ninja",
    "-DSTANDALONE_BUILD=$standalone_switch",
    "-DCMAKE_BUILD_TYPE=Release",
    "-DBUILD_TESTING=$tests_switch"
)

if (-not [string]::IsNullOrWhiteSpace($compilerLauncher)) {
    $cmakeArgs += "-DCMAKE_C_COMPILER_LAUNCHER=$compilerLauncher"
    $cmakeArgs += "-DCMAKE_CXX_COMPILER_LAUNCHER=$compilerLauncher"
    $msg += " (compiler launcher: $compilerLauncher)"
}

$colconArgs = @("build")

if (-not [string]::IsNullOrWhiteSpace($build_base)) {
    $colconArgs += @("--build-base", "$build_base")
    $msg += " (build base: $build_base)"
}

$colconArgs += @(
    "--parallel-workers", "$parallelWorkers",
    "--merge-install",
    "--event-handlers", "console_direct+",
    "--cmake-args"
) + $cmakeArgs

$msg += " (workers: $parallelWorkers, generator: Ninja)"

Write-Host $msg -ForegroundColor Green
colcon @colconArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
