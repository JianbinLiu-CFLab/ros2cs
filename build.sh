#!/usr/bin/env bash
set -euo pipefail

display_usage() {
    echo "Usage: "
    echo "build.sh [--with-tests] [--standalone]"
    echo ""
    echo "Options:"
    echo "--with-tests - build with tests."
    echo "--standalone - standalone version"
}

if [ -z "${ROS_DISTRO:-}" ]; then
    echo "Source your ros2 distro first (foxy, galactic, humble, jazzy or rolling are supported)"
    exit 1
fi

TESTS=OFF
MSG="Build started."
STANDALONE=OFF
PARALLEL_WORKERS="${ROS2CS_PARALLEL_WORKERS:-}"

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
  ''|*[!0-9]*)
    echo "ROS2CS_PARALLEL_WORKERS must be a positive integer"
    exit 1
    ;;
esac

if [ "$PARALLEL_WORKERS" -lt 1 ]; then
  echo "ROS2CS_PARALLEL_WORKERS must be a positive integer"
  exit 1
fi

COMPILER_LAUNCHER="${ROS2CS_COMPILER_LAUNCHER:-}"
if [ -z "$COMPILER_LAUNCHER" ] && command -v ccache >/dev/null 2>&1; then
  COMPILER_LAUNCHER="ccache"
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

MSG="$MSG (workers: $PARALLEL_WORKERS)"

echo "$MSG"
colcon build \
--parallel-workers "$PARALLEL_WORKERS" \
--merge-install \
--event-handlers console_direct+ \
--cmake-args \
"${CMAKE_ARGS[@]}"
