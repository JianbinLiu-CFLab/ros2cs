# Copyright 2019-2021 Robotec.ai
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#    http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
#
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Added generator entrypoint tests for import, failure, and CMake naming contracts.
# - Registered clean generation wrapper coverage in the package test suite.

cmake_minimum_required(VERSION 3.22)

project(rosidl_generator_cs)

find_package(ament_cmake REQUIRED)
find_package(rosidl_cmake REQUIRED)
find_package(rmw_implementation_cmake REQUIRED)

if(BUILD_TESTING)
  find_package(ament_cmake_pytest REQUIRED)
endif()

ament_export_dependencies(ament_cmake)
ament_export_dependencies(rosidl_cmake)
ament_export_dependencies(rmw_implementation_cmake)

ament_index_register_resource("rosidl_generator_packages")

ament_python_install_package(${PROJECT_NAME})

ament_package(
  CONFIG_EXTRAS
    "cmake/rosidl_generator_cs_get_typesupports.cmake"
    "cmake/register_cs.cmake"
    "rosidl_generator_cs-extras.cmake.in"
)

install(
  PROGRAMS bin/rosidl_generator_cs
  DESTINATION lib/rosidl_generator_cs
)

install(
  DIRECTORY cmake resource
  DESTINATION share/${PROJECT_NAME}
)

if(BUILD_TESTING)
  ament_add_pytest_test(test_generator_entrypoints test/test_generator_entrypoints.py)
  ament_add_pytest_test(test_native_template_safety test/test_native_template_safety.py)
endif()
