#!/usr/bin/env bash
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added ROS environment checks and preserved failing test-result exit codes.
# - Documented why this script does not use set -e.
# - Added custom colcon build/install base forwarding.

# Keep -e disabled so colcon test-result still runs and prints diagnostics after colcon test fails.
set -u

if [ -z "${ROS_DISTRO:-}" ]; then
    echo "Source your ros2 distro first."
    exit 1
fi

if [[ ! "$ROS_DISTRO" =~ ^[a-z][a-z0-9_]*$ ]]; then
    echo "Invalid ROS_DISTRO value: '$ROS_DISTRO'."
    exit 1
fi

display_usage() {
    echo "Usage: "
    echo "test.sh [--build-base PATH] [--install-base PATH]"
    echo ""
    echo "Options:"
    echo "--build-base PATH - optional colcon build base directory."
    echo "--install-base PATH - optional colcon install base directory."
}

BUILD_BASE="${ROS2CS_BUILD_BASE:-}"
INSTALL_BASE="${ROS2CS_INSTALL_BASE:-}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        -b|--build-base)
            if [ $# -lt 2 ] || [ -z "$2" ]; then
                echo "--build-base requires a non-empty path"
                display_usage
                exit 1
            fi
            BUILD_BASE="$2"
            shift 2
            ;;
        -i|--install-base)
            if [ $# -lt 2 ] || [ -z "$2" ]; then
                echo "--install-base requires a non-empty path"
                display_usage
                exit 1
            fi
            INSTALL_BASE="$2"
            shift 2
            ;;
        -h|--help)
            display_usage
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            display_usage
            exit 1
            ;;
    esac
done

if ! command -v colcon >/dev/null 2>&1; then
    echo "Can't find colcon. Source your ROS 2 environment or install colcon first."
    exit 1
fi

TEST_ARGS=(test --merge-install --packages-select ros2cs_tests)
RESULT_ARGS=(test-result --verbose)

if [ -n "$BUILD_BASE" ]; then
    TEST_ARGS+=(--build-base "$BUILD_BASE")
    RESULT_ARGS+=(--test-result-base "$BUILD_BASE")
fi

if [ -n "$INSTALL_BASE" ]; then
    TEST_ARGS+=(--install-base "$INSTALL_BASE")
fi

colcon "${TEST_ARGS[@]}"
test_exit_code=$?

colcon "${RESULT_ARGS[@]}"
result_exit_code=$?

if [ "$test_exit_code" -ne 0 ]; then
    exit "$test_exit_code"
fi

exit "$result_exit_code"
