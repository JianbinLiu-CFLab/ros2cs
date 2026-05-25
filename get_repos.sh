#!/usr/bin/env bash
set -euo pipefail

if [ -z "${ROS_DISTRO:-}" ]; then
    echo "Can't detect ROS2 version. Source your ros2 distro first."
    exit 1
fi

if [[ ! "$ROS_DISTRO" =~ ^[a-z][a-z0-9_]*$ ]]; then
    echo "Invalid ROS_DISTRO value: '$ROS_DISTRO'."
    exit 1
fi

if [[ $# -gt 1 || ( $# -eq 1 && "$1" != "--get-custom-messages" ) ]]; then
    echo "Unknown option: ${1:-}"
    exit 1
fi

repos_file="ros2_${ROS_DISTRO}.repos"
if [[ ! -f "$repos_file" ]]; then
    echo "Can't find repos file: '$repos_file'."
    exit 1
fi

echo "Detected ROS2 ${ROS_DISTRO}. Getting required repos from '$repos_file'"
vcs import src < "$repos_file"

if [ "${1:-}" = "--get-custom-messages" ]; then
    if [[ ! -f "custom_messages.repos" ]]; then
        echo "Can't find custom repos file: 'custom_messages.repos'."
        exit 1
    fi
    echo -e "\nGetting custom messages from 'custom_messages.repos'."
    vcs import src < "custom_messages.repos"
fi
