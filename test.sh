#!/usr/bin/env bash
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added ROS environment checks and preserved failing test-result exit codes.

set -u

if [ -z "${ROS_DISTRO:-}" ]; then
    echo "Source your ros2 distro first."
    exit 1
fi

if ! command -v colcon >/dev/null 2>&1; then
    echo "Can't find colcon. Source your ROS 2 environment or install colcon first."
    exit 1
fi

colcon test --merge-install --packages-select ros2cs_tests
test_exit_code=$?

colcon test-result --verbose
result_exit_code=$?

if [ "$test_exit_code" -ne 0 ]; then
    exit "$test_exit_code"
fi

exit "$result_exit_code"
