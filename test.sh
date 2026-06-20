#!/usr/bin/env bash
# Based on upstream RobotecAI ros2cs scripts, Apache-2.0.
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added ROS environment checks and preserved failing test-result exit codes.
# - Documented why this script does not use set -e.
# - Added custom colcon build/install base forwarding.
# - Added test evidence metadata output.
# - Added build-distro marker validation and zero-test-result rejection.
# - Cleared stale colcon test result XML before each test run.
# - Aligned local test package selection with CI.

# Runs the local ros2cs test gate for rosidl_generator_cs and ros2cs_tests.
# The script intentionally separates colcon test from colcon test-result so
# failing tests still print detailed diagnostics and zero-test runs fail closed.

# Keep -e disabled so colcon test-result still runs and prints diagnostics after colcon test fails.
set -u

if [ -z "${ROS_DISTRO:-}" ]; then
    echo "Source your ros2 distro first."
    exit 1
fi

# Validate to prevent path injection; ROS_DISTRO is interpolated into marker and repos filenames.
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

get_effective_install_base() {
    if [ -z "$INSTALL_BASE" ]; then
        printf "%s/install" "$PWD"
        return
    fi

    case "$INSTALL_BASE" in
        /*)
            printf "%s" "$INSTALL_BASE"
            ;;
        *)
            printf "%s/%s" "$PWD" "$INSTALL_BASE"
            ;;
    esac
}

get_git_commit() {
    git rev-parse HEAD 2>/dev/null || printf "unknown"
}

assert_test_distro_matches_build() {
    marker_path="$EFFECTIVE_INSTALL_BASE/.ros2cs_build_distro"
    if [ ! -f "$marker_path" ]; then
        echo "Missing build distro marker '$marker_path'. Rebuild ros2cs with the current build script before running tests."
        exit 1
    fi

    recorded_distro="$(tr -d '\r\n' < "$marker_path")"
    if [ -z "$recorded_distro" ]; then
        echo "Build distro marker '$marker_path' is empty."
        exit 1
    fi

    if [ "$recorded_distro" != "$ROS_DISTRO" ]; then
        echo "Build distro marker '$recorded_distro' does not match current ROS_DISTRO '$ROS_DISTRO'. Rebuild or pass matching --install-base/--build-base paths."
        exit 1
    fi
}

count_colcon_tests() {
    python3 - "$EFFECTIVE_BUILD_BASE" <<'PY'
import pathlib
import sys
import xml.etree.ElementTree as ET

root = pathlib.Path(sys.argv[1])
count = 0
if root.exists():
    for path in root.rglob("*.xml"):
        try:
            tree = ET.parse(path)
        except ET.ParseError:
            continue
        count += len(tree.findall(".//testcase"))
print(count)
PY
}

clear_colcon_test_results() {
    if [ ! -d "$EFFECTIVE_BUILD_BASE" ]; then
        return
    fi

    find "$EFFECTIVE_BUILD_BASE" -type d \( -name test_results -o -name Testing \) -prune -exec rm -rf {} +
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
        printf "test_count=%s\n" "$test_count"
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
EFFECTIVE_INSTALL_BASE="$(get_effective_install_base)"
assert_test_distro_matches_build
clear_colcon_test_results
# --merge-install matches the build layout and keeps install/setup.* sourcing simple.
TEST_ARGS=(test --merge-install --packages-select rosidl_generator_cs ros2cs_tests)
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
if ! test_count="$(count_colcon_tests)"; then
    echo "Failed to count colcon test cases."
    exit 1
fi

write_test_evidence

case "$test_count" in
    ''|*[!0-9]*)
        echo "Invalid colcon test count: '$test_count'."
        exit 1
        ;;
esac

if [ "$test_count" -le 0 ]; then
    echo "No test cases were executed; failing test gate."
    exit 1
fi

if [ "$test_exit_code" -ne 0 ]; then
    exit "$test_exit_code"
fi

exit "$result_exit_code"
