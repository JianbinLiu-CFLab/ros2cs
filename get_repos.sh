#!/usr/bin/env bash
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added fail-fast validation for ROS_DISTRO, repository files, and custom message imports.
# - Added configurable vcs import worker count through ROS2CS_VCS_WORKERS.

set -euo pipefail

SCRIPT_SOURCE="${BASH_SOURCE[0]}"
case "$SCRIPT_SOURCE" in
    */*) SCRIPT_DIR="${SCRIPT_SOURCE%/*}" ;;
    *) SCRIPT_DIR="." ;;
esac
SCRIPT_DIR="$(cd "$SCRIPT_DIR" && pwd)"

if [ -z "${ROS_DISTRO:-}" ]; then
    echo "Can't detect ROS2 version. Source your ros2 distro first."
    exit 1
fi

if [[ ! "$ROS_DISTRO" =~ ^[a-z][a-z0-9_]*$ ]]; then
    echo "Invalid ROS_DISTRO value: '$ROS_DISTRO'."
    exit 1
fi

GET_CUSTOM_MESSAGES=0
VCS_WORKERS="${ROS2CS_VCS_WORKERS:-}"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --get-custom-messages)
            GET_CUSTOM_MESSAGES=1
            shift
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

if [ -z "$VCS_WORKERS" ]; then
    if command -v nproc >/dev/null 2>&1; then
        VCS_WORKERS="$(nproc)"
    elif command -v getconf >/dev/null 2>&1; then
        VCS_WORKERS="$(getconf _NPROCESSORS_ONLN)"
    else
        VCS_WORKERS="1"
    fi
fi

case "$VCS_WORKERS" in
    *[!0-9]*)
        echo "ROS2CS_VCS_WORKERS must be a positive integer"
        exit 1
        ;;
esac

if [ "$VCS_WORKERS" -lt 1 ]; then
    echo "ROS2CS_VCS_WORKERS must be a positive integer"
    exit 1
fi

repos_file="${SCRIPT_DIR}/ros2_${ROS_DISTRO}.repos"
if [[ ! -f "$repos_file" ]]; then
    echo "Can't find repos file: '$repos_file'."
    exit 1
fi

echo "Detected ROS2 ${ROS_DISTRO}. Getting required repos from '$repos_file' (workers: $VCS_WORKERS)"
vcs import --workers "$VCS_WORKERS" "${SCRIPT_DIR}/src" < "$repos_file"

if [ "$GET_CUSTOM_MESSAGES" -eq 1 ]; then
    custom_repos_file="${SCRIPT_DIR}/custom_messages.repos"
    if [[ ! -f "$custom_repos_file" ]]; then
        echo "Can't find custom repos file: '$custom_repos_file'."
        exit 1
    fi
    echo -e "\nGetting custom messages from '$custom_repos_file'."
    vcs import --workers "$VCS_WORKERS" "${SCRIPT_DIR}/src" < "$custom_repos_file"
fi
