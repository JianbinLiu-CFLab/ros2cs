# Copyright 2026 Jianbin Liu.
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

cmake_minimum_required(VERSION 3.5)

project(ros2cs_spin_benchmarks C)

find_package(ament_cmake REQUIRED)
find_package(ros2cs_common REQUIRED)
find_package(ros2cs_core REQUIRED)
find_package(std_msgs REQUIRED)
find_package(dotnet_cmake_module REQUIRED)

set(CSHARP_TARGET_FRAMEWORK "net10.0")
set(CSBUILD_TOOL "DotNetCore")

find_package(DotNETExtra REQUIRED)

set(_assemblies_dep_dlls
    ${ros2cs_common_ASSEMBLIES_DLL}
    ${ros2cs_core_ASSEMBLIES_DLL}
    ${std_msgs_ASSEMBLIES_DLL}
)

add_dotnet_executable(ros2cs_spin_benchmark
  Ros2csSpinBenchmark.cs
  INCLUDE_DLLS
  ${_assemblies_dep_dlls}
)

install_dotnet(ros2cs_spin_benchmark DESTINATION lib/${PROJECT_NAME}/dotnet)

ament_package()
