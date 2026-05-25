
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

Write-Host $msg -ForegroundColor Green
colcon build --merge-install --event-handlers console_direct+ --cmake-args -DSTANDALONE_BUILD=$standalone_switch -DCMAKE_BUILD_TYPE=Release -DBUILD_TESTING=$tests_switch
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
