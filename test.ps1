$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrEmpty($Env:ROS_DISTRO)) {
    Write-Host "Source your ros2 distro first." -ForegroundColor Red
    exit 1
}

if (-not (Get-Command colcon -ErrorAction SilentlyContinue)) {
    Write-Host "Can't find colcon. Source your ROS 2 environment or install colcon first." -ForegroundColor Red
    exit 1
}

colcon test --merge-install --packages-select ros2cs_tests
$testExitCode = $LASTEXITCODE

colcon test-result --verbose
$resultExitCode = $LASTEXITCODE

if ($testExitCode -ne 0) {
    exit $testExitCode
}

exit $resultExitCode
