$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added fail-fast validation for ROS_DISTRO, repository files, and custom message imports.

$scriptPath = split-path -parent $MyInvocation.MyCommand.Definition

if (([string]::IsNullOrEmpty($Env:ROS_DISTRO)))
{
    Write-Host "Can't detect ROS2 version. Source your ros2 distro first." -ForegroundColor Red
    exit 1
}

if ($Env:ROS_DISTRO -notmatch '^[a-z][a-z0-9_]*$')
{
    Write-Host "Invalid ROS_DISTRO value: '$Env:ROS_DISTRO'." -ForegroundColor Red
    exit 1
}

$getCustomMessages = $false
foreach ($arg in $args)
{
    switch ($arg)
    {
        "--get-custom-messages" { $getCustomMessages = $true }
        default
        {
            Write-Host "Unknown option: '$arg'." -ForegroundColor Red
            exit 1
        }
    }
}

$src_path = Join-Path -Path $scriptPath -ChildPath "\src"
$repos_file = Join-Path -Path $scriptPath -ChildPath "\ros2_$Env:ROS_DISTRO.repos"
$custom_repos_file = Join-Path -Path $scriptPath -ChildPath "\custom_messages.repos"
if (Test-Path -Path $repos_file) {
    $repos_file = Resolve-Path -Path $repos_file
    Write-Host "Detected ROS2 $Env:ROS_DISTRO. Getting required repos from $repos_file" -ForegroundColor Green
    vcs import --input $repos_file $src_path
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    if ($getCustomMessages) {
        if (-Not (Test-Path -Path $custom_repos_file)) {
            Write-Host "Can't find custom repos file: '$custom_repos_file'." -ForegroundColor Red
            exit 1
        }
        Write-Host "Getting custom messages from $custom_repos_file" -ForegroundColor Green
        vcs import --input $custom_repos_file $src_path
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
} else {
    Write-Host "Can't find repos file: '$repos_file'." -ForegroundColor Red
    exit 1
}
