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
# - Reused generated message/service struct C objects through package-local OBJECT libraries.
# - Preserved per-message/per-service native shared libraries for compatibility while removing repeated struct compilation.
# - Shortened generated CMake logical target names while preserving runtime native library names.
# - Loaded ament_cmake explicitly for ROS 2 Lyrical rosidl extension contexts.

find_package(ament_cmake REQUIRED)
find_package(rosidl_generator_c REQUIRED)
find_package(rosidl_typesupport_c REQUIRED)
find_package(rosidl_typesupport_interface REQUIRED)
find_package(ament_cmake_export_assemblies REQUIRED)
find_package(dotnet_cmake_module REQUIRED)
# DotNETExtra is a CMake module provided by dotnet_cmake_module, not a separate
# ROS package dependency.
find_package(DotNETExtra REQUIRED) # add_dotnet_library
find_package(ros2cs_common REQUIRED)

# Some ROSIDL extension contexts do not expose ament_target_dependencies.
# Fall back to package-provided targets, include dirs, libraries, and link flags
# so generated native targets still link on older ROS 2/CMake package layouts.
function(_rosidl_generator_cs_target_dependencies target)
  if(COMMAND ament_target_dependencies)
    ament_target_dependencies(${target} ${ARGN})
    return()
  endif()

  foreach(_pkg_name ${ARGN})
    if(NOT ${_pkg_name}_FOUND)
      message(FATAL_ERROR "Package '${_pkg_name}' was not found before linking ${target}")
    endif()

    set(_dependency_targets "")
    foreach(_candidate ${${_pkg_name}_TARGETS} ${${_pkg_name}_INTERFACES})
      if(TARGET "${_candidate}")
        list(APPEND _dependency_targets "${_candidate}")
      endif()
    endforeach()

    if(_dependency_targets)
      target_link_libraries(${target} ${_dependency_targets})
    else()
      target_compile_definitions(${target} PUBLIC ${${_pkg_name}_DEFINITIONS})
      target_include_directories(${target} PUBLIC ${${_pkg_name}_INCLUDE_DIRS})

      set(_dependency_libraries "")
      foreach(_library ${${_pkg_name}_LIBRARIES})
        if(NOT "${${_pkg_name}_LIBRARY_DIRS}" STREQUAL "")
          if(NOT IS_ABSOLUTE ${_library} OR NOT EXISTS ${_library})
            unset(_resolved_library CACHE)
            find_library(
              _resolved_library
              NAMES ${_library}
              PATHS ${${_pkg_name}_LIBRARY_DIRS}
              NO_DEFAULT_PATH
            )
            if(_resolved_library)
              set(_library ${_resolved_library})
            endif()
          endif()
        endif()
        list(APPEND _dependency_libraries ${_library})
      endforeach()

      if(_dependency_libraries)
        target_link_libraries(${target} ${_dependency_libraries})
      endif()

      set(_dependency_link_flags ${${_pkg_name}_LINK_FLAGS})
      foreach(_link_flag IN LISTS _dependency_link_flags)
        set_property(TARGET ${target} APPEND_STRING PROPERTY LINK_FLAGS " ${_link_flag} ")
      endforeach()
    endif()
  endforeach()
endfunction()

# Get a list of typesupport implementations from valid rmw implementations.
rosidl_generator_cs_get_typesupports(_typesupport_impls)

if(_typesupport_impls STREQUAL "")
  message(WARNING "No valid typesupport for .NET generator. .NET messages will not be generated.")
  return()
endif()

foreach(_typesupport_impl ${_typesupport_impls})
  find_package(${_typesupport_impl} REQUIRED)
endforeach()

set(_output_path "${CMAKE_CURRENT_BINARY_DIR}/rosidl_generator_cs/${PROJECT_NAME}")
set(_generated_msg_cs_files "")
set(_generated_msg_c_files "")
set(_generated_msg_c_ts_files "")
set(_generated_srv_cs_files "")
set(_generated_srv_c_files "")
set(_generated_srv_c_ts_files "")

if(NOT WIN32)
  if(CMAKE_CXX_COMPILER_ID STREQUAL "GNU")
    set(CMAKE_SHARED_LINKER_FLAGS "${CMAKE_SHARED_LINKER_FLAGS} -Wl,--no-undefined")
  elseif(CMAKE_CXX_COMPILER_ID MATCHES "Clang")
    set(CMAKE_SHARED_LINKER_FLAGS "${CMAKE_SHARED_LINKER_FLAGS} -Wl,-undefined,error")
  endif()
endif()

# For each IDL file
foreach(_idl_file ${rosidl_generate_interfaces_ABS_IDL_FILES})
  get_filename_component(_parent_folder "${_idl_file}" DIRECTORY)
  get_filename_component(_parent_folder "${_parent_folder}" NAME)
  get_filename_component(_msg_name "${_idl_file}" NAME_WE)
  get_filename_component(_ext "${_idl_file}" EXT)
  string_camel_case_to_lower_case_underscore("${_msg_name}" _module_name)

  if(_parent_folder STREQUAL "msg")
    list(APPEND _generated_msg_cs_files
      "${_output_path}/${_parent_folder}/${_module_name}.cs"
    )
    list(APPEND _generated_msg_c_files
      "${_output_path}/${_parent_folder}/${_module_name}_s.c"
    )
    foreach(_typesupport_impl ${_typesupport_impls})
        list_append_unique(_generated_msg_c_ts_files
          "${_output_path}/${_parent_folder}/${_module_name}.ep.${_typesupport_impl}.c"
        )
        list(APPEND _type_support_by_generated_msg_c_files "${_typesupport_impl}")
    endforeach()
  elseif(_parent_folder STREQUAL "srv")
    list(APPEND _generated_srv_cs_files
      "${_output_path}/${_parent_folder}/${_module_name}.cs"
    )
    list(APPEND _generated_srv_c_files
      "${_output_path}/${_parent_folder}/${_module_name}_s.c"
    )
    foreach(_typesupport_impl ${_typesupport_impls})
        list_append_unique(_generated_srv_c_ts_files
          "${_output_path}/${_parent_folder}/${_module_name}.ep.${_typesupport_impl}.c"
        )
        list(APPEND _type_support_by_generated_srv_c_files "${_typesupport_impl}")
    endforeach()
  elseif(_parent_folder STREQUAL "action")
    message(STATUS "rosidl_generator_cs: action type '${_msg_name}' skipped (not yet supported)")
  else()
    message(FATAL_ERROR "Interface file with unknown parent folder: ${_idl_file}")
  endif()
endforeach()

if( (_generated_msg_c_files STREQUAL "") AND (_generated_srv_c_files STREQUAL "") )
  return()
endif()

set(_dependency_files "")
set(_dependencies "")
foreach(_pkg_name ${rosidl_generate_interfaces_DEPENDENCY_PACKAGE_NAMES})
  foreach(_idl_file ${${_pkg_name}_INTERFACE_FILES})
    set(_abs_idl_file "${${_pkg_name}_DIR}/../${_idl_file}")
    normalize_path(_abs_idl_file "${_abs_idl_file}")
    list(APPEND _dependency_files "${_abs_idl_file}")
    list(APPEND _dependencies "${_pkg_name}:${_abs_idl_file}")
  endforeach()
endforeach()

set(target_dependencies
  "${rosidl_generator_cs_BIN}"
  ${rosidl_generator_cs_GENERATOR_FILES}
  "${rosidl_generator_cs_TEMPLATE_DIR}/idl.c.em"
  "${rosidl_generator_cs_TEMPLATE_DIR}/idl_typesupport.c.em"
  "${rosidl_generator_cs_TEMPLATE_DIR}/idl.cs.em"
  "${rosidl_generator_cs_TEMPLATE_DIR}/msg.c.em"
  "${rosidl_generator_cs_TEMPLATE_DIR}/msg_typesupport.c.em"
  "${rosidl_generator_cs_TEMPLATE_DIR}/msg.cs.em"
  "${rosidl_generator_cs_TEMPLATE_DIR}/srv.c.em"
  "${rosidl_generator_cs_TEMPLATE_DIR}/srv_typesupport.c.em"
  "${rosidl_generator_cs_TEMPLATE_DIR}/srv.cs.em"
  ${rosidl_generate_interfaces_ABS_IDL_FILES}
  ${_dependency_files}
)

foreach(dep ${target_dependencies})
  if(NOT EXISTS "${dep}")
    message(FATAL_ERROR "Target dependency '${dep}' does not exist")
  endif()
endforeach()

# ROSIDL generator arguments
set(generator_arguments_file "${CMAKE_CURRENT_BINARY_DIR}/rosidl_generator_cs__arguments.json")
rosidl_write_generator_arguments(
  "${generator_arguments_file}"
  PACKAGE_NAME "${PROJECT_NAME}"
  IDL_TUPLES "${rosidl_generate_interfaces_IDL_TUPLES}"
  ROS_INTERFACE_DEPENDENCIES "${_dependencies}"
  OUTPUT_DIR "${_output_path}"
  TEMPLATE_DIR "${rosidl_generator_cs_TEMPLATE_DIR}"
  TARGET_DEPENDENCIES ${target_dependencies}
)

file(MAKE_DIRECTORY "${_output_path}")

if(NOT rosidl_generator_cs_CLEAN_GENERATE)
  message(FATAL_ERROR "rosidl_generator_cs clean generator wrapper was not registered")
endif()

set(_generated_cs_outputs
  ${_generated_msg_cs_files}
  ${_generated_msg_c_files}
  ${_generated_msg_c_ts_files}
  ${_generated_srv_cs_files}
  ${_generated_srv_c_files}
  ${_generated_srv_c_ts_files}
)
set(_generated_cs_outputs_file "${CMAKE_CURRENT_BINARY_DIR}/rosidl_generator_cs__outputs.txt")
file(WRITE "${_generated_cs_outputs_file}" "")
foreach(_generated_cs_output IN LISTS _generated_cs_outputs)
  file(APPEND "${_generated_cs_outputs_file}" "${_generated_cs_output}\n")
endforeach()

message(STATUS "Generating C# code for ROS interfaces ${_generated_msg_cs_files} and ${_generated_srv_cs_files}")
set(ros2_distro "$ENV{ROS_DISTRO}")
# Foxy/Galactic do not reliably provide the imported Python3::Interpreter target
# in this extension context, so keep their legacy PYTHON_EXECUTABLE path.
if(ros2_distro STREQUAL "foxy" OR ros2_distro STREQUAL "galactic")
  set(PYTHON_CMD ${PYTHON_EXECUTABLE})
else()
  set(PYTHON_CMD Python3::Interpreter)
endif()

add_custom_command(
  OUTPUT ${_generated_msg_cs_files} ${_generated_msg_c_files} ${_generated_msg_c_ts_files} ${_generated_srv_cs_files} ${_generated_srv_c_files} ${_generated_srv_c_ts_files}
  COMMAND ${PYTHON_CMD}
  ARGS "${rosidl_generator_cs_CLEAN_GENERATE}"
  --outputs-file "${_generated_cs_outputs_file}"
  --generator "${rosidl_generator_cs_BIN}"
  --
  --generator-arguments-file "${generator_arguments_file}"
  --typesupport-impls "${_typesupport_impls}"
  --cs-build-tool "${CSBUILD_TOOL}"
  DEPENDS ${target_dependencies} "${_generated_cs_outputs_file}"
  COMMENT "Generating C# code for ROS interfaces"
  VERBATIM
)

message(STATUS "Adding custom target")

set(_target_suffix "__cs")
if(TARGET ${rosidl_generate_interfaces_TARGET}${_target_suffix})
  message(WARNING "Custom target ${rosidl_generate_interfaces_TARGET}${_target_suffix} already exists")
else()
  add_custom_target(
    ${rosidl_generate_interfaces_TARGET}${_target_suffix}
    DEPENDS
    ${_generated_msg_cs_files}
    ${_generated_msg_c_ts_files}
    ${_generated_msg_c_files}
    ${_generated_srv_cs_files}
    ${_generated_srv_c_ts_files}
    ${_generated_srv_c_files}
  )
endif()

set_property(
  SOURCE
  ${_generated_msg_cs_files} ${_generated_msg_c_files} ${_generated_msg_c_ts_files} ${_generated_srv_cs_files} ${_generated_srv_c_files} ${_generated_srv_c_ts_files}
  PROPERTY GENERATED 1)

set(_extension_compile_flags "")
if(NOT WIN32)
  set(_extension_compile_flags "-Wall -Wextra")
endif()

set(_extension_link_flags "")
if(NOT WIN32)
  if(CMAKE_COMPILER_IS_GNUCXX)
    set(_extension_link_flags "-Wl,--no-undefined")
  elseif(CMAKE_CXX_COMPILER_ID MATCHES "Clang")
    set(_extension_link_flags "-Wl,-undefined,error")
  endif()
endif()

set(_generated_msg_c_structs_target "")
if(_generated_msg_c_files)
  set(_generated_msg_c_structs_target "${PROJECT_NAME}__cs_msg_structs")
  # Generated C struct sources are shared by all per-message targets in this package,
  # so compile them once as an OBJECT library and reuse the objects below.
  add_library(${_generated_msg_c_structs_target} OBJECT
    ${_generated_msg_c_files}
  )
  set_target_properties(
    ${_generated_msg_c_structs_target}
    PROPERTIES
    COMPILE_FLAGS             "${_extension_compile_flags}"
    # These OBJECT files are linked into generated shared libraries.
    POSITION_INDEPENDENT_CODE ON
  )
  target_include_directories(${_generated_msg_c_structs_target}
    PUBLIC
    ${CMAKE_CURRENT_BINARY_DIR}/rosidl_generator_c
    ${CMAKE_CURRENT_BINARY_DIR}/rosidl_generator_cs
  )
  _rosidl_generator_cs_target_dependencies(${_generated_msg_c_structs_target}
    "rosidl_generator_c"
    "rosidl_typesupport_c"
    "rosidl_typesupport_interface"
  )
  foreach(_pkg_name ${rosidl_generate_interfaces_DEPENDENCY_PACKAGE_NAMES})
    _rosidl_generator_cs_target_dependencies(${_generated_msg_c_structs_target}
      ${_pkg_name}
    )
  endforeach()
  if(TARGET ${PROJECT_NAME}__rosidl_generator_c)
    add_dependencies(${_generated_msg_c_structs_target}
      ${PROJECT_NAME}__rosidl_generator_c
    )
  endif()
endif()

set(_generated_srv_c_structs_target "")
if(_generated_srv_c_files)
  set(_generated_srv_c_structs_target "${PROJECT_NAME}__cs_srv_structs")
  # Generated C struct sources are shared by all per-service targets in this package,
  # so compile them once as an OBJECT library and reuse the objects below.
  add_library(${_generated_srv_c_structs_target} OBJECT
    ${_generated_srv_c_files}
  )
  set_target_properties(
    ${_generated_srv_c_structs_target}
    PROPERTIES
    COMPILE_FLAGS             "${_extension_compile_flags}"
    # These OBJECT files are linked into generated shared libraries.
    POSITION_INDEPENDENT_CODE ON
  )
  target_include_directories(${_generated_srv_c_structs_target}
    PUBLIC
    ${CMAKE_CURRENT_BINARY_DIR}/rosidl_generator_c
    ${CMAKE_CURRENT_BINARY_DIR}/rosidl_generator_cs
  )
  _rosidl_generator_cs_target_dependencies(${_generated_srv_c_structs_target}
    "rosidl_generator_c"
    "rosidl_typesupport_c"
    "rosidl_typesupport_interface"
  )
  foreach(_pkg_name ${rosidl_generate_interfaces_DEPENDENCY_PACKAGE_NAMES})
    _rosidl_generator_cs_target_dependencies(${_generated_srv_c_structs_target}
      ${_pkg_name}
    )
  endforeach()
  if(TARGET ${PROJECT_NAME}__rosidl_generator_c)
    add_dependencies(${_generated_srv_c_structs_target}
      ${PROJECT_NAME}__rosidl_generator_c
    )
  endif()
endif()

list(LENGTH _generated_msg_c_ts_files _generated_msg_c_ts_count)
if(_generated_msg_c_ts_count GREATER 0)
  math(EXPR _generated_msg_c_ts_last_index "${_generated_msg_c_ts_count} - 1")
  foreach(_file_index RANGE 0 ${_generated_msg_c_ts_last_index})
    list(GET _generated_msg_c_ts_files ${_file_index} _generated_msg_c_ts_file)
    list(GET _type_support_by_generated_msg_c_files ${_file_index} _typesupport_impl)
  get_filename_component(_full_folder "${_generated_msg_c_ts_file}" DIRECTORY)
  get_filename_component(_package_folder "${_full_folder}" DIRECTORY)
  get_filename_component(_package_name "${_package_folder}" NAME)
  get_filename_component(_parent_folder "${_full_folder}" NAME)
  get_filename_component(_base_msg_name "${_generated_msg_c_ts_file}" NAME_WE)
  # The generator writes entrypoint files as <module>.ep.<typesupport>.c; strip
  # the entrypoint/typesupport suffix so runtime DLLs keep the historical
  # <package>_<message>__<typesupport>_native naming expected by generated C#.
  string(REGEX REPLACE "\\.ep\\..*$" "" _module_name "${_base_msg_name}")

  set(_runtime_name "${_package_name}_${_module_name}__${_typesupport_impl}")
  # Keep the runtime DLL name stable for generated C# LoadLibrary calls, but use
  # a short logical CMake target name so MSVC object paths stay below CMake limits.
  set(_target_name "${PROJECT_NAME}__cs_msg_ts_${_file_index}")

  add_library(${_target_name} SHARED
    "${_generated_msg_c_ts_file}"
    # Keep one shared library per message/typesupport for compatibility; only repeated struct compilation is removed.
    $<TARGET_OBJECTS:${_generated_msg_c_structs_target}>
  )

  set(_destination_dir "${_output_path}/${_parent_folder}")

  set_target_properties(
    ${_target_name}
    PROPERTIES
    COMPILE_FLAGS                           "${_extension_compile_flags}"
    OUTPUT_NAME                             "${_runtime_name}_native"
    RUNTIME_OUTPUT_DIRECTORY                ${_destination_dir}
    RUNTIME_OUTPUT_DIRECTORY_DEBUG          ${_destination_dir}
    RUNTIME_OUTPUT_DIRECTORY_RELEASE        ${_destination_dir}
    RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO ${_destination_dir}
    RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL     ${_destination_dir}
    LIBRARY_OUTPUT_DIRECTORY                ${_destination_dir}
    LIBRARY_OUTPUT_DIRECTORY_DEBUG          ${_destination_dir}
    LIBRARY_OUTPUT_DIRECTORY_RELEASE        ${_destination_dir}
    LIBRARY_OUTPUT_DIRECTORY_RELWITHDEBINFO ${_destination_dir}
    LIBRARY_OUTPUT_DIRECTORY_MINSIZEREL     ${_destination_dir}
  )

  message(STATUS "Link libraries: ${PROJECT_NAME}__${_typesupport_impl}")
  target_link_libraries(${_target_name}
    ${PROJECT_NAME}__${_typesupport_impl}
    ${_extension_link_flags}
    ${PROJECT_NAME}__rosidl_generator_c
  )

  # rosidl_cmake newer than 2.5.0 provides rosidl_get_typesupport_target; older
  # distro layouts need rosidl_target_interfaces.
  if(${rosidl_cmake_VERSION} VERSION_GREATER 2.5.0)
    rosidl_get_typesupport_target(c_typesupport_target "${PROJECT_NAME}" "rosidl_typesupport_c")
    target_link_libraries(${_target_name} "${c_typesupport_target}")
  else()
    rosidl_target_interfaces(${_target_name} ${PROJECT_NAME} rosidl_typesupport_c)
  endif()

  target_include_directories(${_target_name}
    PUBLIC
    ${CMAKE_CURRENT_BINARY_DIR}/rosidl_generator_c
    ${CMAKE_CURRENT_BINARY_DIR}/rosidl_generator_cs
  )

  _rosidl_generator_cs_target_dependencies(${_target_name}
    "rosidl_generator_c"
    "rosidl_generator_cs"
    "rosidl_typesupport_c"
    "rosidl_typesupport_interface"
  )

  foreach(_pkg_name ${rosidl_generate_interfaces_DEPENDENCY_PACKAGE_NAMES})
    _rosidl_generator_cs_target_dependencies(${_target_name}
      ${_pkg_name}
    )
  endforeach()

  add_dependencies(${_target_name}
    ${rosidl_generate_interfaces_TARGET}__${_typesupport_impl}
  )

  if(NOT rosidl_generate_interfaces_SKIP_INSTALL)
    install(TARGETS ${_target_name}
      ARCHIVE DESTINATION lib
      LIBRARY DESTINATION lib
      RUNTIME DESTINATION bin
    )
  endif()

  endforeach()
endif()

list(LENGTH _generated_srv_c_ts_files _generated_srv_c_ts_count)
if(_generated_srv_c_ts_count GREATER 0)
  math(EXPR _generated_srv_c_ts_last_index "${_generated_srv_c_ts_count} - 1")
  foreach(_file_index RANGE 0 ${_generated_srv_c_ts_last_index})
    list(GET _generated_srv_c_ts_files ${_file_index} _generated_srv_c_ts_file)
    list(GET _type_support_by_generated_srv_c_files ${_file_index} _typesupport_impl)
  get_filename_component(_full_folder "${_generated_srv_c_ts_file}" DIRECTORY)
  get_filename_component(_package_folder "${_full_folder}" DIRECTORY)
  get_filename_component(_package_name "${_package_folder}" NAME)
  get_filename_component(_parent_folder "${_full_folder}" NAME)
  get_filename_component(_base_srv_name "${_generated_srv_c_ts_file}" NAME_WE)
  # Services keep an explicit _srv_ infix in runtime DLL names to disambiguate
  # service support libraries from message support libraries with the same base name.
  string(REGEX REPLACE "\\.ep\\..*$" "" _module_name "${_base_srv_name}")

  set(_runtime_name "${_package_name}_srv_${_module_name}__${_typesupport_impl}")
  # Keep the runtime DLL name stable for generated C# LoadLibrary calls, but use
  # a short logical CMake target name so MSVC object paths stay below CMake limits.
  set(_target_name "${PROJECT_NAME}__cs_srv_ts_${_file_index}")

  add_library(${_target_name} SHARED
    "${_generated_srv_c_ts_file}"
    # Keep one shared library per service/typesupport for compatibility; only repeated struct compilation is removed.
    $<TARGET_OBJECTS:${_generated_srv_c_structs_target}>
  )

  set(_destination_dir "${_output_path}/${_parent_folder}")

  set_target_properties(
    ${_target_name}
    PROPERTIES
    COMPILE_FLAGS                           "${_extension_compile_flags}"
    OUTPUT_NAME                             "${_runtime_name}_native"
    RUNTIME_OUTPUT_DIRECTORY                ${_destination_dir}
    RUNTIME_OUTPUT_DIRECTORY_DEBUG          ${_destination_dir}
    RUNTIME_OUTPUT_DIRECTORY_RELEASE        ${_destination_dir}
    RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO ${_destination_dir}
    RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL     ${_destination_dir}
    LIBRARY_OUTPUT_DIRECTORY                ${_destination_dir}
    LIBRARY_OUTPUT_DIRECTORY_DEBUG          ${_destination_dir}
    LIBRARY_OUTPUT_DIRECTORY_RELEASE        ${_destination_dir}
    LIBRARY_OUTPUT_DIRECTORY_RELWITHDEBINFO ${_destination_dir}
    LIBRARY_OUTPUT_DIRECTORY_MINSIZEREL     ${_destination_dir}
  )

  message(STATUS "Link libraries: ${PROJECT_NAME}__${_typesupport_impl}")
  target_link_libraries(${_target_name}
    ${PROJECT_NAME}__${_typesupport_impl}
    ${_extension_link_flags}
    ${PROJECT_NAME}__rosidl_generator_c
  )

  # rosidl_cmake newer than 2.5.0 provides rosidl_get_typesupport_target; older
  # distro layouts need rosidl_target_interfaces.
  if(${rosidl_cmake_VERSION} VERSION_GREATER 2.5.0)
    rosidl_get_typesupport_target(c_typesupport_target "${PROJECT_NAME}" "rosidl_typesupport_c")
    target_link_libraries(${_target_name} "${c_typesupport_target}")
  else()
    rosidl_target_interfaces(${_target_name} ${PROJECT_NAME} rosidl_typesupport_c)
  endif()

  target_include_directories(${_target_name}
    PUBLIC
    ${CMAKE_CURRENT_BINARY_DIR}/rosidl_generator_c
    ${CMAKE_CURRENT_BINARY_DIR}/rosidl_generator_cs
  )

  _rosidl_generator_cs_target_dependencies(${_target_name}
    "rosidl_generator_c"
    "rosidl_generator_cs"
    "rosidl_typesupport_c"
    "rosidl_typesupport_interface"
  )

  foreach(_pkg_name ${rosidl_generate_interfaces_DEPENDENCY_PACKAGE_NAMES})
    _rosidl_generator_cs_target_dependencies(${_target_name}
      ${_pkg_name}
    )
  endforeach()

  add_dependencies(${_target_name}
    ${rosidl_generate_interfaces_TARGET}__${_typesupport_impl}
  )

  if(NOT rosidl_generate_interfaces_SKIP_INSTALL)
    install(TARGETS ${_target_name}
      ARCHIVE DESTINATION lib
      LIBRARY DESTINATION lib
      RUNTIME DESTINATION bin
    )
  endif()

  endforeach()
endif()

set(_assembly_deps_dll "")
list(APPEND _assembly_deps_dll ${ros2cs_common_ASSEMBLIES_DLL})

foreach(_pkg_name ${rosidl_generate_interfaces_DEPENDENCY_PACKAGE_NAMES})
  find_package(${_pkg_name} REQUIRED)
  foreach(_assembly_dep ${${_pkg_name}_ASSEMBLIES_DLL})
    list_append_unique(_assembly_deps_dll "${_assembly_dep}")
  endforeach()
endforeach()

add_dotnet_library(${PROJECT_NAME}_assembly
  SOURCES
  ${_generated_msg_cs_files} ${_generated_srv_cs_files}
  INCLUDE_DLLS
  ${_assembly_deps_dll}
)

add_dependencies(${PROJECT_NAME}_assembly
  "${rosidl_generate_interfaces_TARGET}${_target_suffix}"
)

if(NOT rosidl_generate_interfaces_SKIP_INSTALL)
  if(_generated_msg_cs_files OR _generated_srv_cs_files)
    install_dotnet(${PROJECT_NAME}_assembly DESTINATION "lib/dotnet")
    ament_export_assemblies_dll("lib/dotnet/${PROJECT_NAME}_assembly.dll")
  endif()
endif()
