# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added ROS environment checks and preserved failing test-result exit codes.
# - Added custom colcon build/install base forwarding.

Param (
    [Parameter(Mandatory=$false)][switch]$help=$false,
    [Parameter(Mandatory=$false)][string]$build_base=$Env:ROS2CS_BUILD_BASE,
    [Parameter(Mandatory=$false)][string]$install_base=$Env:ROS2CS_INSTALL_BASE
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($help) {
    Write-Host "Usage: .\test.ps1 [-build_base PATH] [-install_base PATH]"
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -build_base PATH    Optional colcon build base directory. Can also be set with ROS2CS_BUILD_BASE."
    Write-Host "  -install_base PATH  Optional colcon install base directory. Can also be set with ROS2CS_INSTALL_BASE."
    exit 0
}

if ([string]::IsNullOrEmpty($Env:ROS_DISTRO)) {
    Write-Host "Source your ros2 distro first." -ForegroundColor Red
    exit 1
}

if ($Env:ROS_DISTRO -notmatch '^[a-z][a-z0-9_]*$') {
    Write-Host "Invalid ROS_DISTRO value: '$Env:ROS_DISTRO'." -ForegroundColor Red
    exit 1
}

if (-not (Get-Command colcon -ErrorAction SilentlyContinue)) {
    Write-Host "Can't find colcon. Source your ROS 2 environment or install colcon first." -ForegroundColor Red
    exit 1
}

$testArgs = @("test", "--merge-install", "--packages-select", "ros2cs_tests")
$resultArgs = @("test-result", "--verbose")

if (-not [string]::IsNullOrWhiteSpace($build_base)) {
    $testArgs += @("--build-base", "$build_base")
    $resultArgs += @("--test-result-base", "$build_base")
}

if (-not [string]::IsNullOrWhiteSpace($install_base)) {
    $testArgs += @("--install-base", "$install_base")
}

colcon @testArgs
$testExitCode = $LASTEXITCODE

colcon @resultArgs
$resultExitCode = $LASTEXITCODE

if ($testExitCode -ne 0) {
    exit $testExitCode
}

exit $resultExitCode
