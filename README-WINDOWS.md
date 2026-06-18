# ROS2CS - Windows 10 / Windows 10 LTSC / Windows 11

> Modifications Copyright (c) 2026 Jianbin Liu.
>
> Modifications by Jianbin Liu:
> - Documented the Jazzy/FastRTPS RTI Connext DDS Micro probe stderr as a known non-blocking environment warning.
> - Documented Ninja as the required Windows Jazzy generator policy for VS 2026 / VS 18 toolchains.
> - Updated the current Windows verification status for Windows 10 LTSC + ROS 2 Jazzy and .NET 10 tests/examples.
> - Updated tests/examples target framework references to .NET 10.

## Building

### Current verification status

Current GREEN evidence for this maintenance branch is:

- Windows 10 IoT Enterprise LTSC 2021 (`10.0.19044`).
- ROS 2 Jazzy from the local pixi-based Windows distribution.
- `RMW_IMPLEMENTATION=rmw_fastrtps_cpp`.
- MSVC compiler with Ninja generator.
- `ros2cs` source workspace build/test.
- `ros2cs_common`, `ros2cs_core`, and generated message assemblies on `netstandard2.0`.
- `ros2cs_tests` and `ros2cs_examples` on `net10.0`.
- Latest public maintenance preview: [`v0.6.0-jazzy-preview.1`](https://github.com/JianbinLiu-CFLab/ros2cs/releases/tag/v0.6.0-jazzy-preview.1).

Windows 11 is an expected target but was not the OS used for the current local validation. Older ROS 2 distributions in this README are legacy context unless fresh evidence is added.

ROS 2 Lyrical is available as a preview probe line through `ros2_lyrical.repos`
and `D:\ros2unity\tools\Enter-Ros2LyricalEnv.py`. It is not a replacement for
the Jazzy maintenance preview until the Lyrical build, test, R2FU artifact, and
Unity smoke plans have passed.

### Prerequisites

*  ROS2 installed on the system. For this maintenance branch, the verified target is ROS 2 Jazzy.
*  vcstool package - [see here](https://github.com/dirk-thomas/vcstool)
*  .NET 10 SDK for tests/examples.
*  `ros2cs_common`, `ros2cs_core`, and generated message assemblies remain compatible with `netstandard2.0`.
*  For tests only: NUnit test infrastructure as configured by `ros2cs_tests`.

### Important notices

- Windows [path length is limited to 260 characters](https://docs.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation) by default. A good solution is to modify your `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem` registry key `LongPathsEnabled` to 1. This way you will avoid path length issues. Alternatively, you need to clone your repo to `C:\dev` into `r2cs` folder or a similar shallow path to avoid this issue during build. **Cloning into longer path will cause compilation errors!**

- For building and running, an MSVC/Visual Studio configured PowerShell terminal must be used. Standard PowerShell may not have the compiler and Windows SDK paths configured. Visual Studio 2022 and Visual Studio 2026 / VS 18 toolchains are both acceptable compiler environments when Ninja is used.

- Windows Jazzy builds should use the Ninja generator with MSVC (`-G Ninja` or `CMAKE_GENERATOR=Ninja`). Visual Studio 2026 / VS 18 environments are valid compiler environments, but the Jazzy-pinned `colcon_cmake` and CMake versions do not support auto-selecting a Visual Studio 18 generator. Do not rely on colcon's automatic Visual Studio generator detection.

- A powershell terminal with administrator privileges is required for **Windows** and **ros2 galactic**. This is because python packages installation requires a privilage for creating symlinks. More about this issue: [github issue](https://github.com/ament/ament_cmake/issues/350).

- There is a bug with hardcoded include exports in some **ros2 galactic** packages on **Windows**. Easiest workaround is to create a `C:\ci\ws\install\include` directory in your system. More about this bug and proposed workarounds: [github issue](https://github.com/ros2/rclcpp/issues/1688#issuecomment-858467147).

- Sometimes it is required to set NuGet package feed to nuget.org: `dotnet nuget add source --name nuget.org https://api.nuget.org/v3/index.json` in order to resolve some missing packages for `ros2cs` project.

### Steps

- Clone this project.
- Source your ROS2 installation.
  - Legacy example: `C:\dev\ros2_foxy\local_setup.ps1`
  - Current maintenance target: ROS 2 Jazzy.
  - Preview probe target: ROS 2 Lyrical via `D:\ros2unity\tools\Enter-Ros2LyricalEnv.py`.
- Navigate to the top project folder and pull required repositories (`get_repos.ps1`)
  - You can run script with `--get-custom-messages` argument to fetch extra messages from `custom_messages.repos` file.
  - It will use `vcstool` to download required ROS2 packages. By default, this will get repositories as set in `${ROS_DISTRO}`.
- Build package (`build.ps1`)
  - It invokes `colcon_build` with `--merge-install` argument to simplify libraries installation
  - You can build tests by adding `-with_tests` argument
- To test your build please check main readme [Testing section](README.md#testing)

### Build profiles

Use `.\build.ps1 -with_tests` for full validation or first builds. It uses Ninja,
parallel workers, and optional compiler launcher support.

For daily ros2cs core/test iteration after dependencies are already built:

```powershell
colcon build --packages-select ros2cs_core ros2cs_tests --merge-install
```

For generator/template changes:

```powershell
colcon build --packages-select rosidl_generator_cs std_msgs test_msgs example_interfaces ros2cs_tests --merge-install
```

Avoid deleting `build/`, `install/`, or using CMake clean flags unless the
dependency graph or CMake cache is actually stale.

Record validation commands, exit codes, and key output in your local release or
maintenance notes before making platform support claims.

### Standalone version (Windows)

By default, Windows build process generates standalone libraries in `install/standalone` directory.
You can disable this feature by setting CMake option `STANDALONE_BUILD` to `OFF`.

To run standalone application you must deploy it with libraries from both `install/bin` and `install/standalone`.

To run examples with standalone build you should modify `PATH`  environment variable so your executable will find all the required libraries (if executable lies in a different directory than libraries).
Additionally all libraries need to be in a visible path, since they are loaded dynamically at runtime.

## Troubleshooting

- Tests are not working ('charmap' codec can't decode byte) on Windows

Problem may occur on non english version of Windows. This error is caused by impossibility in decoding `dotnet` output by ament tools.

**Fix**: Change your `dotnet` output to english by temporarily renaming your localization directory (`pl` to `pl.bak`, `fr` to `fr.bak` etc.) in your `dotnet` sdk directory.

- Known non-blocking stderr: `ERRORFailed to load RTI Connext DDS Micro`

Windows Jazzy + FastRTPS builds may print this line while generating interface typesupport. It comes from the ROS 2 `rmw_implementation` loader probing the installed `rmw_connextddsmicro_cpp` plugin without an RTI Connext DDS Micro runtime. It is not a ros2cs code path.

Treat it as non-blocking environment noise only when all of these are true:

- `RMW_IMPLEMENTATION=rmw_fastrtps_cpp`
- `colcon build` exits with code 0
- `colcon test` exits with code 0 and the CTest `Test.xml` / NUnit output has 0 failures
- the message appears only in interface package stderr, not in ros2cs C# or native shim compile/link stderr

Re-evaluate if you switch to `rmw_connextddsmicro_cpp`, install RTI Connext DDS Micro runtime, or any of the conditions above stop being true.

- `Unknown / unsupported VS version '18.0'`

This indicates colcon/CMake tried to infer a Visual Studio generator from a Visual Studio 2026 / VS 18 environment. Use Ninja instead:

```powershell
colcon build --cmake-args -G Ninja
```

Set `CMAKE_GENERATOR=Ninja` in the sourced ROS 2 / MSVC environment, or pass
`--cmake-args -G Ninja` explicitly in reproducible command logs.

**If no solution of your problem is present in the section above, please make sure to check out `ROS2 For Unity` [Troubleshooting section](https://github.com/RobotecAI/ros2-for-unity/blob/master/README-WINDOWS.md#build-troubleshooting)**
