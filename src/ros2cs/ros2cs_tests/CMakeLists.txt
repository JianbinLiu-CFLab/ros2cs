#
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Updated the C# test target framework to .NET 10.
# - Preserved the final newline for consistent generated build files.
# - Added a generated message finalizer child-process probe.
# - Added a NativeLibraryHandle finalizer child-process probe.
# - Added isolated ros2cs_common primitive coverage.
# - Raised the CTest timeout for the expanded managed test suite.
# - Added a Windows extended-length native loader fixture for ros2cs_common coverage.
#

cmake_minimum_required(VERSION 3.6)
project(ros2cs_tests)

if(BUILD_TESTING)
  find_package(ros2cs_common REQUIRED)
  find_package(ros2cs_core REQUIRED)
  find_package(ament_cmake REQUIRED)
  find_package(dotnet_cmake_module REQUIRED)

  # Build tests on .NET 10 while core/common remain netstandard2.0.
  set(CSHARP_TARGET_FRAMEWORK "net10.0")

  set(CSBUILD_TOOL "DotNetCore")
  find_package(DotNETExtra REQUIRED)

  find_package(std_msgs REQUIRED)
  find_package(test_msgs REQUIRED)
  find_package(example_interfaces REQUIRED)

  set(DOTNET_OUTPUT_PATH
    ${CSHARP_BUILDER_OUTPUT_PATH}/${CSHARP_TARGET_FRAMEWORK}/
  )

  # MSVC cannot link directly above MAX_PATH. Build the fixture normally; the test copies it
  # into this isolated long path before exercising LoadLibraryW.
  set(ROS2CS_NATIVE_FIXTURE_SOURCE_DIRECTORY
    "${CMAKE_CURRENT_BINARY_DIR}/native_loader_fixture"
  )
  # Keep the copied fixture safely above MAX_PATH even when the workspace uses a short build root.
  set(ROS2CS_LONG_PATH_NATIVE_FIXTURE_DIRECTORY
    "${CMAKE_CURRENT_BINARY_DIR}/long_path_native_loader_fixture/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/cccccccccccccccccccccccccccccccccccccccccccccccc/dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
  )
  set(ROS2CS_LONG_PATH_NATIVE_FIXTURE_LIBRARY_STEM "ros2cs_long_path_native_fixture")
  file(TO_NATIVE_PATH "${ROS2CS_NATIVE_FIXTURE_SOURCE_DIRECTORY}" ROS2CS_NATIVE_FIXTURE_SOURCE_DIRECTORY)
  file(TO_NATIVE_PATH "${ROS2CS_LONG_PATH_NATIVE_FIXTURE_DIRECTORY}" ROS2CS_LONG_PATH_NATIVE_FIXTURE_DIRECTORY)
  configure_file(
    src/LongPathNativeFixture.cs.in
    "${CMAKE_CURRENT_BINARY_DIR}/LongPathNativeFixture.cs"
    @ONLY
  )

  add_library(${ROS2CS_LONG_PATH_NATIVE_FIXTURE_LIBRARY_STEM} SHARED
    src/LongPathNativeFixture.c
  )
  set_target_properties(${ROS2CS_LONG_PATH_NATIVE_FIXTURE_LIBRARY_STEM} PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY "${ROS2CS_NATIVE_FIXTURE_SOURCE_DIRECTORY}"
    RUNTIME_OUTPUT_DIRECTORY "${ROS2CS_NATIVE_FIXTURE_SOURCE_DIRECTORY}"
  )

  set(ASSEMBLIES_DEP_DLLS
      ${ros2cs_core_ASSEMBLIES_DLL}
      ${ros2cs_common_ASSEMBLIES_DLL}
      ${std_msgs_ASSEMBLIES_DLL}
      ${test_msgs_ASSEMBLIES_DLL}
      ${example_interfaces_ASSEMBLIES_DLL}
  )

  set(TESTS_SRC
    src/AssemblyInfo.cs
    src/ClockTest.cs
    src/CreateNodeTest.cs
    src/InitShutdownTest.cs
    src/LargeMessageTest.cs
    src/MessagesTest.cs
    src/NodeTest.cs
    src/NativeMethodsTest.cs
    src/Ros2csCommonTest.cs
    src/ClientTest.cs
    src/ServiceTest.cs
    src/SubscriptionTest.cs
    src/TestUtils.cs
    "${CMAKE_CURRENT_BINARY_DIR}/LongPathNativeFixture.cs"
  )

  add_dotnet_test(ros2cs_tests
    ${TESTS_SRC}
    INCLUDE_DLLS
    ${ASSEMBLIES_DEP_DLLS}
  )
  add_dependencies(ros2cs_tests ${ROS2CS_LONG_PATH_NATIVE_FIXTURE_LIBRARY_STEM})
  set_tests_properties(ros2cs_tests_test PROPERTIES TIMEOUT 180)

  # Keep the finalizer probe as a real child process: a native crash during finalization
  # must be isolated from the in-process NUnit runner above.
  set(FINALIZER_PROBE_TARGET ros2cs_generated_message_finalizer_probe)
  add_dotnet_executable(${FINALIZER_PROBE_TARGET}
    src/GeneratedMessageFinalizerProbe.cs
    INCLUDE_DLLS
    ${ASSEMBLIES_DEP_DLLS}
  )

  ament_add_test(
    ${FINALIZER_PROBE_TARGET}_test
    GENERATE_RESULT_FOR_RETURN_CODE_ZERO
    WORKING_DIRECTORY "${DOTNET_OUTPUT_PATH}"
    COMMAND dotnet "${FINALIZER_PROBE_TARGET}.dll"
  )
  set_tests_properties(
    ${FINALIZER_PROBE_TARGET}_test
    PROPERTIES PASS_REGULAR_EXPRESSION "GENERATED_MESSAGE_FINALIZER_PROBE_PASS"
  )

  # Keep NativeLibraryHandle finalization isolated too. The fake loader checks ownership
  # semantics without trying to recreate Unity's partially unloaded native host.
  set(NATIVE_LIBRARY_HANDLE_FINALIZER_PROBE_TARGET ros2cs_native_library_handle_finalizer_probe)
  add_dotnet_executable(${NATIVE_LIBRARY_HANDLE_FINALIZER_PROBE_TARGET}
    src/NativeLibraryHandleFinalizerProbe.cs
    INCLUDE_DLLS
    ${ASSEMBLIES_DEP_DLLS}
  )

  ament_add_test(
    ${NATIVE_LIBRARY_HANDLE_FINALIZER_PROBE_TARGET}_test
    GENERATE_RESULT_FOR_RETURN_CODE_ZERO
    WORKING_DIRECTORY "${DOTNET_OUTPUT_PATH}"
    COMMAND dotnet "${NATIVE_LIBRARY_HANDLE_FINALIZER_PROBE_TARGET}.dll"
  )
  set_tests_properties(
    ${NATIVE_LIBRARY_HANDLE_FINALIZER_PROBE_TARGET}_test
    PROPERTIES PASS_REGULAR_EXPRESSION "NATIVE_LIBRARY_HANDLE_FINALIZER_PROBE_PASS"
  )

  install_dotnet(${PROJECT_NAME} DESTINATION lib/tests)

  ament_package()
endif()
