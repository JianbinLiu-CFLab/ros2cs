<#
.SYNOPSIS
    Runs the local ros2cs test gate.
.DESCRIPTION
    Executes colcon test for rosidl_generator_cs and ros2cs_tests, then always
    runs colcon test-result so failing tests still print diagnostics. The script
    also rejects stale/zero test results and writes local test evidence metadata.
.PARAMETER build_base
    Optional colcon build base directory. Can also be set with ROS2CS_BUILD_BASE.
    Defaults to build-$ROS_DISTRO to match build.ps1 isolation.
.PARAMETER install_base
    Optional colcon install base directory. Can also be set with ROS2CS_INSTALL_BASE.
    Defaults to install-$ROS_DISTRO to avoid cross-distro runtime DLL shadowing.
.PARAMETER help
    Show help and exit.

Based on upstream RobotecAI ros2cs scripts, Apache-2.0.
Modifications Copyright (c) 2026 Jianbin Liu.

Modifications by Jianbin Liu:
- Added ROS environment checks and preserved failing test-result exit codes.
- Added custom colcon build/install base forwarding.
- Added test evidence metadata output.
- Added build-distro marker validation and zero-test-result rejection.
- Cleared stale colcon test result XML before each test run.
- Aligned local test package selection with CI.
- Isolated default build/install bases per ROS_DISTRO to prevent mixed Jazzy/Lyrical test environments.

Fail-fast PowerShell settings are enabled immediately after parameter parsing;
colcon exit codes are captured explicitly later so test-result diagnostics still run.
#>

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
    Write-Host "  -build_base PATH    Optional colcon build base directory. Defaults to build-`$ROS_DISTRO."
    Write-Host "  -install_base PATH  Optional colcon install base directory. Defaults to install-`$ROS_DISTRO."
    exit 0
}

if ([string]::IsNullOrEmpty($Env:ROS_DISTRO)) {
    Write-Host "Source your ros2 distro first." -ForegroundColor Red
    exit 1
}

# Validate to prevent path injection; ROS_DISTRO is interpolated into marker and repos filenames.
if ($Env:ROS_DISTRO -notmatch '^[a-z][a-z0-9_]*$') {
    Write-Host "Invalid ROS_DISTRO value: '$Env:ROS_DISTRO'." -ForegroundColor Red
    exit 1
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
        [int]$ResultExitCode,
        [int]$TestCount
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
        "colcon_test_result_exit_code=$ResultExitCode",
        "test_count=$TestCount"
    )
    Set-Content -LiteralPath $infoPath -Value $lines -Encoding UTF8
}

function Assert-TestDistroMatchesBuild {
    param([string]$InstallBase)

    $markerPath = Join-Path $InstallBase ".ros2cs_build_distro"
    if (-not (Test-Path -LiteralPath $markerPath)) {
        throw "Missing build distro marker '$markerPath'. Rebuild ros2cs with the current build script before running tests."
    }

    $recordedDistro = (Get-Content -LiteralPath $markerPath -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($recordedDistro)) {
        throw "Build distro marker '$markerPath' is empty."
    }

    if ($recordedDistro -ne $Env:ROS_DISTRO) {
        throw "Build distro marker '$recordedDistro' does not match current ROS_DISTRO '$Env:ROS_DISTRO'. Rebuild or pass matching -install_base/-build_base paths."
    }
}

function Get-ColconTestCount {
    param([string]$BuildBase)

    $count = 0
    if (-not (Test-Path -LiteralPath $BuildBase)) {
        return 0
    }

    Get-ChildItem -LiteralPath $BuildBase -Recurse -Filter "*.xml" -File -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            [xml]$xml = Get-Content -LiteralPath $_.FullName -Raw
            $nodes = $xml.SelectNodes("//testcase")
            if ($null -ne $nodes) {
                $count += $nodes.Count
            }
        } catch {
            # Non-JUnit XML files are ignored; colcon test-result will report malformed result files.
        }
    }
    return $count
}

function Clear-ColconTestResults {
    param([string]$BuildBase)

    if (-not (Test-Path -LiteralPath $BuildBase)) {
        return
    }

    Get-ChildItem -LiteralPath $BuildBase -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "test_results" -or $_.Name -eq "Testing" } |
        Sort-Object FullName -Descending |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
}

if (-not (Get-Command colcon -ErrorAction SilentlyContinue)) {
    Write-Host "Can't find colcon. Source your ROS 2 environment or install colcon first." -ForegroundColor Red
    exit 1
}

$effectiveBuildBase = Get-EffectiveBuildBase -BuildBase $build_base
$effectiveInstallBase = Get-EffectiveInstallBase -InstallBase $install_base
Assert-TestDistroMatchesBuild -InstallBase $effectiveInstallBase
Clear-ColconTestResults -BuildBase $effectiveBuildBase
# --merge-install matches the build layout and keeps install/setup.* sourcing simple.
$testArgs = @(
    "test",
    "--build-base", "$effectiveBuildBase",
    "--install-base", "$effectiveInstallBase",
    "--merge-install",
    "--packages-select", "rosidl_generator_cs", "ros2cs_tests"
)
$resultArgs = @("test-result", "--test-result-base", "$effectiveBuildBase", "--verbose")

colcon @testArgs
$testExitCode = $LASTEXITCODE

colcon @resultArgs
$resultExitCode = $LASTEXITCODE
$testCount = Get-ColconTestCount -BuildBase $effectiveBuildBase

Write-TestEvidence -BuildBase $effectiveBuildBase -TestExitCode $testExitCode -ResultExitCode $resultExitCode -TestCount $testCount

if ($testCount -le 0) {
    Write-Host "No test cases were executed; failing test gate." -ForegroundColor Red
    exit 1
}

if ($testExitCode -ne 0) {
    exit $testExitCode
}

exit $resultExitCode
