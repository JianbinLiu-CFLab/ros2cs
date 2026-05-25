#
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Modifications by Jianbin Liu:
# - Updated the C# test target framework to .NET 8.
# - Preserved the final newline for consistent generated build files.
#

cmake_minimum_required(VERSION 3.6)
project(ros2cs_tests)

if(BUILD_TESTING)
  find_package(ros2cs_common REQUIRED)
  find_package(ros2cs_core REQUIRED)
  find_package(ament_cmake REQUIRED)
  find_package(dotnet_cmake_module REQUIRED)

  # Build tests on .NET 8 while core/common remain netstandard2.0.
  set(CSHARP_TARGET_FRAMEWORK "net8.0")

  set(CSBUILD_TOOL "DotNetCore")
  find_package(DotNETExtra REQUIRED)

  find_package(std_msgs REQUIRED)
  find_package(test_msgs REQUIRED)
  find_package(example_interfaces REQUIRED)

  set(DOTNET_OUTPUT_PATH
    ${CSHARP_BUILDER_OUTPUT_PATH}/${CSHARP_TARGET_FRAMEWORK}/
  )

  set(ASSEMBLIES_DEP_DLLS
      ${ros2cs_core_ASSEMBLIES_DLL}
      ${ros2cs_common_ASSEMBLIES_DLL}
      ${std_msgs_ASSEMBLIES_DLL}
      ${test_msgs_ASSEMBLIES_DLL}
      ${example_interfaces_ASSEMBLIES_DLL}
  )

  set(TESTS_SRC
    src/ClockTest.cs
    src/CreateNodeTest.cs
    src/InitShutdownTest.cs
    src/LargeMessageTest.cs
    src/MessagesTest.cs
    src/NodeTest.cs
    src/NativeMethodsTest.cs
    src/ClientTest.cs
    src/ServiceTest.cs
    src/SubscriptionTest.cs
    src/TestUtils.cs
  )

  add_dotnet_test(ros2cs_tests
    ${TESTS_SRC}
    INCLUDE_DLLS
    ${ASSEMBLIES_DEP_DLLS}
  )

  install_dotnet(${PROJECT_NAME} DESTINATION lib/tests)

  ament_package()
endif()
