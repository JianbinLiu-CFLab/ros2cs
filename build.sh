#!/usr/bin/env bash
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added explicit parallel worker selection with ROS2CS_PARALLEL_WORKERS override.
# - Added optional compiler launcher support through ROS2CS_COMPILER_LAUNCHER or ccache auto-detection.
# - Added optional short colcon build base support through --build-base or ROS2CS_BUILD_BASE.
# - Added optional colcon install base support through --install-base or ROS2CS_INSTALL_BASE.
# - Defaulted Linux/macOS builds to Ninja and made the colcon event handler configurable.
# - Added build evidence metadata and a default install-base distro guard.

set -euo pipefail

display_usage() {
    echo "Usage: "
    echo "build.sh [--with-tests] [--standalone] [--build-base PATH] [--install-base PATH]"
    echo ""
    echo "Options:"
    echo "--with-tests - build with tests."
    echo "--standalone - standalone version"
    echo "--build-base PATH - optional colcon build base directory."
    echo "--install-base PATH - optional colcon install base directory."
}

if [ -z "${ROS_DISTRO:-}" ]; then
    echo "Source your ros2 distro first (foxy, galactic, humble, jazzy, lyrical or rolling are supported)"
    exit 1
fi

if [[ ! "$ROS_DISTRO" =~ ^[a-z][a-z0-9_]*$ ]]; then
    echo "Invalid ROS_DISTRO value: '$ROS_DISTRO'."
    exit 1
fi

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

assert_default_install_base_matches_distro() {
  if [ "$EXPLICIT_INSTALL_BASE" = "1" ]; then
    return
  fi

  marker_path="$EFFECTIVE_INSTALL_BASE/.ros2cs_build_distro"
  if [ ! -f "$marker_path" ]; then
    return
  fi

  recorded_distro="$(tr -d '\r\n' < "$marker_path")"
  if [ -n "$recorded_distro" ] && [ "$recorded_distro" != "$ROS_DISTRO" ]; then
    echo "Default install base '$EFFECTIVE_INSTALL_BASE' was last built for ROS_DISTRO='$recorded_distro', but current ROS_DISTRO='$ROS_DISTRO'. Pass --install-base for an isolated build or remove the default install directory intentionally."
    exit 1
  fi
}

write_build_evidence() {
  mkdir -p "$EFFECTIVE_INSTALL_BASE"
  printf "%s\n" "$ROS_DISTRO" > "$EFFECTIVE_INSTALL_BASE/.ros2cs_build_distro"
  {
    printf "ros2cs_commit=%s\n" "$(get_git_commit)"
    printf "ros_distro=%s\n" "$ROS_DISTRO"
    printf "build_timestamp_utc=%s\n" "$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
    printf "repo_path=%s\n" "$PWD"
    printf "install_base=%s\n" "$EFFECTIVE_INSTALL_BASE"
    printf "standalone=%s\n" "$STANDALONE"
    printf "build_testing=%s\n" "$TESTS"
  } > "$EFFECTIVE_INSTALL_BASE/ros2cs_build_info.txt"
}

TESTS=OFF
MSG="Build started."
STANDALONE=OFF
PARALLEL_WORKERS="${ROS2CS_PARALLEL_WORKERS:-}"
BUILD_BASE="${ROS2CS_BUILD_BASE:-}"
INSTALL_BASE="${ROS2CS_INSTALL_BASE:-}"
EVENT_HANDLER="${ROS2CS_EVENT_HANDLER:-console_cohesion+}"

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
    *)    # unknown option
      echo "Unknown option: $1"
      display_usage
      exit 1
      ;;
  esac
done

EXPLICIT_INSTALL_BASE=0
if [ -n "$INSTALL_BASE" ]; then
  EXPLICIT_INSTALL_BASE=1
fi
EFFECTIVE_INSTALL_BASE="$(get_effective_install_base)"
assert_default_install_base_matches_distro

CMAKE_ARGS=(
  -G Ninja
  -DCMAKE_BUILD_TYPE=Release
  -DSTANDALONE_BUILD="$STANDALONE"
  -DBUILD_TESTING="$TESTS"
  -DCMAKE_SHARED_LINKER_FLAGS="-Wl,-rpath,'\$ORIGIN',-rpath=.,--disable-new-dtags"
)

if [ -n "${COLCON_PYTHON_EXECUTABLE:-}" ]; then
  CMAKE_ARGS+=("-DPython3_EXECUTABLE:FILEPATH=${COLCON_PYTHON_EXECUTABLE}")
fi

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

if [ -n "$INSTALL_BASE" ]; then
  COLCON_ARGS+=(--install-base "$INSTALL_BASE")
  MSG="$MSG (install base: $INSTALL_BASE)"
fi

MSG="$MSG (workers: $PARALLEL_WORKERS, generator: Ninja, event handler: $EVENT_HANDLER)"

echo "$MSG"
colcon "${COLCON_ARGS[@]}" \
--parallel-workers "$PARALLEL_WORKERS" \
--merge-install \
--event-handlers "$EVENT_HANDLER" \
--cmake-args \
"${CMAKE_ARGS[@]}" || exit $?

write_build_evidence
