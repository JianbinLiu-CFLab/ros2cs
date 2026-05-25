$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

colcon test --merge-install --packages-select ros2cs_tests
$testExitCode = $LASTEXITCODE

colcon test-result --verbose
$resultExitCode = $LASTEXITCODE

if ($testExitCode -ne 0) {
    exit $testExitCode
}

exit $resultExitCode
