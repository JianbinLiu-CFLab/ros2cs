# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added ROS environment checks and preserved failing test-result exit codes.
# - Added custom colcon build/install base forwarding.
# - Added test evidence metadata output.

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

function Get-EffectiveBuildBase {
    param([string]$BuildBase)

    $repoRoot = (Get-Location).Path
    if ([string]::IsNullOrWhiteSpace($BuildBase)) {
        return (Join-Path $repoRoot "build")
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
        # Test evidence should not make local test runs depend on git availability.
    }

    return "unknown"
}

function Write-TestEvidence {
    param(
        [string]$BuildBase,
        [int]$TestExitCode,
        [int]$ResultExitCode
    )

    New-Item -ItemType Directory -Path $BuildBase -Force | Out-Null

    $infoPath = Join-Path $BuildBase "ros2cs_test_info.txt"
    $lines = @(
        "ros2cs_commit=$(Get-GitCommit)",
        "ros_distro=$Env:ROS_DISTRO",
        "test_timestamp_utc=$([DateTimeOffset]::UtcNow.ToString('o'))",
        "repo_path=$((Get-Location).Path)",
        "build_base=$BuildBase",
        "colcon_test_exit_code=$TestExitCode",
        "colcon_test_result_exit_code=$ResultExitCode"
    )
    Set-Content -LiteralPath $infoPath -Value $lines -Encoding UTF8
}

if (-not (Get-Command colcon -ErrorAction SilentlyContinue)) {
    Write-Host "Can't find colcon. Source your ROS 2 environment or install colcon first." -ForegroundColor Red
    exit 1
}

$effectiveBuildBase = Get-EffectiveBuildBase -BuildBase $build_base
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

Write-TestEvidence -BuildBase $effectiveBuildBase -TestExitCode $testExitCode -ResultExitCode $resultExitCode

if ($testExitCode -ne 0) {
    exit $testExitCode
}

exit $resultExitCode
