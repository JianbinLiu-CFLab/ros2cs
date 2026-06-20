#!/usr/bin/env bash
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added ROS environment checks and preserved failing test-result exit codes.
# - Documented why this script does not use set -e.
# - Added custom colcon build/install base forwarding.
# - Added test evidence metadata output.

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

get_effective_build_base() {
    if [ -z "$BUILD_BASE" ]; then
        printf "%s/build" "$PWD"
        return
    fi

    case "$BUILD_BASE" in
        /*)
            printf "%s" "$BUILD_BASE"
            ;;
        *)
            printf "%s/%s" "$PWD" "$BUILD_BASE"
            ;;
    esac
}

get_git_commit() {
    git rev-parse HEAD 2>/dev/null || printf "unknown"
}

write_test_evidence() {
    mkdir -p "$EFFECTIVE_BUILD_BASE"
    {
        printf "ros2cs_commit=%s\n" "$(get_git_commit)"
        printf "ros_distro=%s\n" "$ROS_DISTRO"
        printf "test_timestamp_utc=%s\n" "$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
        printf "repo_path=%s\n" "$PWD"
        printf "build_base=%s\n" "$EFFECTIVE_BUILD_BASE"
        printf "colcon_test_exit_code=%s\n" "$test_exit_code"
        printf "colcon_test_result_exit_code=%s\n" "$result_exit_code"
    } > "$EFFECTIVE_BUILD_BASE/ros2cs_test_info.txt"
}

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

EFFECTIVE_BUILD_BASE="$(get_effective_build_base)"
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

write_test_evidence

if [ "$test_exit_code" -ne 0 ]; then
    exit "$test_exit_code"
fi

exit "$result_exit_code"
