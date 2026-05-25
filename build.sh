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

echo "$MSG"
colcon build \
--merge-install \
--event-handlers console_direct+ \
--cmake-args \
-DCMAKE_BUILD_TYPE=Release \
-DSTANDALONE_BUILD=$STANDALONE \
-DBUILD_TESTING=$TESTS \
-DCMAKE_SHARED_LINKER_FLAGS="-Wl,-rpath,'\$ORIGIN',-rpath=.,--disable-new-dtags"
