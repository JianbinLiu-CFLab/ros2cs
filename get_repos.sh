#!/usr/bin/env bash
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added fail-fast validation for ROS_DISTRO, repository files, and custom message imports.

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

repos_file="${SCRIPT_DIR}/ros2_${ROS_DISTRO}.repos"
if [[ ! -f "$repos_file" ]]; then
    echo "Can't find repos file: '$repos_file'."
    exit 1
fi

echo "Detected ROS2 ${ROS_DISTRO}. Getting required repos from '$repos_file'"
vcs import "${SCRIPT_DIR}/src" < "$repos_file"

if [ "$GET_CUSTOM_MESSAGES" -eq 1 ]; then
    custom_repos_file="${SCRIPT_DIR}/custom_messages.repos"
    if [[ ! -f "$custom_repos_file" ]]; then
        echo "Can't find custom repos file: '$custom_repos_file'."
        exit 1
    fi
    echo -e "\nGetting custom messages from '$custom_repos_file'."
    vcs import "${SCRIPT_DIR}/src" < "$custom_repos_file"
fi
