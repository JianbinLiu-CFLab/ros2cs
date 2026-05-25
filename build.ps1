
<#
.SYNOPSIS
    Builds 'ros2cs'
.DESCRIPTION
    This script runs colcon build
.PARAMETER with_tests
    build with tests
.PARAMETER standalone
    standalone build
#>
Param (
    [Parameter(Mandatory=$false)][switch]$with_tests=$false,
    [Parameter(Mandatory=$false)][switch]$standalone=$false
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-ParallelWorkers {
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

$colconArgs = @(
    "build",
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
