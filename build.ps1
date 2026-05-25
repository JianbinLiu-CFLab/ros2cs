
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

if ([string]::IsNullOrEmpty($Env:ROS_DISTRO)) {
    Write-Host "Source your ros2 distro first (foxy, galactic, humble, jazzy or rolling are supported)" -ForegroundColor Red
    exit 1
}

$msg="Build started."
$tests_switch=0
if($with_tests) {
    $msg+=" (with tests)"
    $tests_switch=1
}
$standalone_switch=0
if($standalone) {
    $msg+=" (standalone)"
    $standalone_switch=1
}

Write-Host $msg -ForegroundColor Green
colcon build --merge-install --event-handlers console_direct+ --cmake-args -DSTANDALONE_BUILD:int=$standalone_switch -DCMAKE_BUILD_TYPE=Release -DBUILD_TESTING:int=$tests_switch --no-warn-unused-cli
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
