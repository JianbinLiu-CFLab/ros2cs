#!/usr/bin/env bash
set -u

colcon test --merge-install --packages-select ros2cs_tests
test_exit_code=$?

colcon test-result --verbose
result_exit_code=$?

if [ "$test_exit_code" -ne 0 ]; then
    exit "$test_exit_code"
fi

exit "$result_exit_code"
