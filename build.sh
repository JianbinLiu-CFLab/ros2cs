#!/usr/bin/env bash
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added explicit parallel worker selection with ROS2CS_PARALLEL_WORKERS override.
# - Added optional compiler launcher support through ROS2CS_COMPILER_LAUNCHER or ccache auto-detection.
# - Added optional short colcon build base support through --build-base or ROS2CS_BUILD_BASE.

set -euo pipefail

display_usage() {
    echo "Usage: "
    echo "build.sh [--with-tests] [--standalone] [--build-base PATH]"
    echo ""
    echo "Options:"
    echo "--with-tests - build with tests."
    echo "--standalone - standalone version"
    echo "--build-base PATH - optional colcon build base directory."
}

if [ -z "${ROS_DISTRO:-}" ]; then
    echo "Source your ros2 distro first (foxy, galactic, humble, jazzy or rolling are supported)"
    exit 1
fi

TESTS=OFF
MSG="Build started."
STANDALONE=OFF
PARALLEL_WORKERS="${ROS2CS_PARALLEL_WORKERS:-}"
BUILD_BASE="${ROS2CS_BUILD_BASE:-}"

# Worker count is a throughput policy; build correctness must not depend on a specific value.
if [ -z "$PARALLEL_WORKERS" ]; then
  if command -v nproc >/dev/null 2>&1; then
    PARALLEL_WORKERS="$(nproc)"
  elif command -v getconf >/dev/null 2>&1; then
    PARALLEL_WORKERS="$(getconf _NPROCESSORS_ONLN)"
  else
    PARALLEL_WORKERS="1"
  fi
fi

case "$PARALLEL_WORKERS" in
  *[!0-9]*)
    echo "ROS2CS_PARALLEL_WORKERS must be a positive integer"
    exit 1
    ;;
esac

if [ "$PARALLEL_WORKERS" -lt 1 ]; then
  echo "ROS2CS_PARALLEL_WORKERS must be a positive integer"
  exit 1
fi

COMPILER_LAUNCHER="${ROS2CS_COMPILER_LAUNCHER:-}"
# Compiler launcher use is best-effort acceleration and must not be required for a correct build.
if [ -z "$COMPILER_LAUNCHER" ] && command -v ccache >/dev/null 2>&1; then
  COMPILER_LAUNCHER="ccache"
elif [ -z "$COMPILER_LAUNCHER" ] && command -v sccache >/dev/null 2>&1; then
  COMPILER_LAUNCHER="sccache"
fi

while [[ $# -gt 0 ]]; do
  key="$1"
  case $key in
    -t|--with-tests)
      TESTS=ON
      MSG="$MSG (with tests)"
      shift # past argument
      ;;
    -s|--standalone)
      STANDALONE=ON
      MSG="$MSG (standalone)"
      shift # past argument
      ;;
    -b|--build-base)
      if [ $# -lt 2 ] || [ -z "$2" ]; then
        echo "--build-base requires a non-empty path"
        display_usage
        exit 1
      fi
      BUILD_BASE="$2"
      shift 2
      ;;
    -h|--help)
      display_usage
      exit 0
      ;;
    *)    # unknown option
      echo "Unknown option: $1"
      display_usage
      exit 1
      ;;
  esac
done

CMAKE_ARGS=(
  -DCMAKE_BUILD_TYPE=Release
  -DSTANDALONE_BUILD="$STANDALONE"
  -DBUILD_TESTING="$TESTS"
  -DCMAKE_SHARED_LINKER_FLAGS="-Wl,-rpath,'\$ORIGIN',-rpath=.,--disable-new-dtags"
)

if [ -n "$COMPILER_LAUNCHER" ]; then
  CMAKE_ARGS+=(
    -DCMAKE_C_COMPILER_LAUNCHER="$COMPILER_LAUNCHER"
    -DCMAKE_CXX_COMPILER_LAUNCHER="$COMPILER_LAUNCHER"
  )
  MSG="$MSG (compiler launcher: $COMPILER_LAUNCHER)"
fi

COLCON_ARGS=(build)
if [ -n "$BUILD_BASE" ]; then
  COLCON_ARGS+=(--build-base "$BUILD_BASE")
  MSG="$MSG (build base: $BUILD_BASE)"
fi

MSG="$MSG (workers: $PARALLEL_WORKERS)"

echo "$MSG"
colcon "${COLCON_ARGS[@]}" \
--parallel-workers "$PARALLEL_WORKERS" \
--merge-install \
--event-handlers console_direct+ \
--cmake-args \
"${CMAKE_ARGS[@]}"
