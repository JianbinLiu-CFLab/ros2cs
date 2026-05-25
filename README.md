Ros2cs
=============

> Modifications Copyright (c) 2026 Jianbin Liu.
>
> Modifications by Jianbin Liu:
> - Documented the current conservative verification status for the Jazzy maintenance branch.
> - Clarified Windows 10 LTSC validation, .NET target framework split, and legacy Ubuntu status.
> - Declared the JianbinLiu-CFLab `main` branch as the maintained integration line.

A C# .NET library for ROS2, including C# implementation of rcl APIs, message generation, tests and examples.

Ros2cs is also an independent part of [Ros2 For Unity](https://github.com/RobotecAI/ros2-for-unity), which enables high-performance communication between simulation and ROS2 robot packages. Follow instructions there instead if you are intending to use ros2cs with Unity3D.

### Maintained integration line

For the JianbinLiu-CFLab fork, `main` is the maintained integration line.

The upstream RobotecAI repository and its historical branches remain the original source and licensing history, but they are no longer used as the active integration target for this Jazzy/R2FU line. Upstream changes should be reviewed and cherry-picked deliberately rather than merged blindly.

Downstream projects should consume:

```text
https://github.com/JianbinLiu-CFLab/ros2cs.git
version: main
```

The latest public maintenance preview is:

```text
v0.3.0-jazzy-preview.1
https://github.com/JianbinLiu-CFLab/ros2cs/releases/tag/v0.3.0-jazzy-preview.1
```

### Features

- A set of core abstractions such as Node, Publisher, Subscription, QoS, Clock
- Comes with support for all standard ros2 messages
- Custom messages can be easily generated from unmodified ROS2 packages
- A logger that can be hooked to your application callbacks (e.g. in Unity3D)

### Current verification status

This maintenance branch keeps the historical ros2cs platform goals, but current GREEN evidence is narrower than the legacy support matrix below.

Verified in the current local validation:

- Windows 10 IoT Enterprise LTSC 2021 (`10.0.19044`) with ROS 2 Jazzy.
- `RMW_IMPLEMENTATION=rmw_fastrtps_cpp`.
- MSVC toolchain with Ninja generator.
- `ros2cs` workspace build/test for the Jazzy source workspace.
- `ros2cs_common`, `ros2cs_core`, and generated message assemblies remain `netstandard2.0`.
- `ros2cs_tests` and `ros2cs_examples` target `net8.0`.

Not yet verified in the current maintenance round:

- Ubuntu 20.04 / 22.04 / 24.04.
- Windows 11.
- ROS 2 Foxy / Galactic / Humble / Rolling on this branch.
- Unity / R2FU runtime import and Player validation.

### Platform goals and legacy matrix

Historical/project OS targets:
- Ubuntu 24.04 (bash)
- Ubuntu 22.04 (bash)
- Ubuntu 20.04 (bash)
- Windows 10 / Windows 10 LTSC (powershell)
- Windows 11 (powershell)

> The current Jazzy validation evidence is Windows 10 LTSC only. Treat Windows 11 and Ubuntu entries as expected or legacy targets until they have fresh build/test evidence.

Historical/project ROS2 distribution targets:
- Jazzy
- Humble
- Galactic
- Foxy

### Flavours

`ros2cs` libraries can be built in two flavors:
- _standalone_ (no ROS2 installation required on the target machine, e.g., your Unity3D simulation server). All required dependencies are installed and can be used e.g., as a complete set of Unity3D plugins.
- _overlay_ (assuming existing (supported) ROS2 installation on the target machine). Only ros2cs libraries and generated messages are installed.

## Building

### Generating custom messages

After cloning the project and importing .repos, you can simply put your message package next to other packages in the `src/ros2` sub-folder. Then, build your project, and you have all messages generated. You can also modify and use the `custom_message.repos` template to automate the process with the `get_repos` script.

### Build instructions

Please follow the OS-specific instructions for your build:

- [Ubuntu Instructions](README-UBUNTU.md) - legacy, not recently verified for this Jazzy maintenance branch
- [Windows Instructions](README-WINDOWS.md) - current validation target is Windows 10 LTSC + Jazzy

## Testing

Make sure your NuGet repositories can resolve the test dependencies used by `ros2cs_tests` (currently NUnit-based). You can call `dotnet nuget list source` to see your current sources for NuGet packages. Please note that `Microsoft Visual Studio Offline Packages` are usually insufficient. You can fix it by adding `nuget.org` repository: `dotnet nuget add source --name nuget.org https://api.nuget.org/v3/index.json`.

- Make sure you built tests (OS-specific build script with `--with-tests` flag).
- Run OS-specific test script:
    - ubuntu:
    ```bash
    ./test.sh
    ```
    - windows:
    ```powershell
    test.ps1
    ```
- Run a manual test with basic listener/publisher examples (you have to source your ROS2 first):
    - ubuntu
    ```bash
    ros2 run ros2cs_examples ros2cs_talker
    ros2 run ros2cs_examples ros2cs_listener
    ```
    - windows
    ```
    ros2 run ros2cs_examples ros2cs_talker.exe
    ros2 run ros2cs_examples ros2cs_listener.exe
    ```
- Run a manual performance test (you have to source your ROS2 first):
    - ubuntu
    ```bash
    ros2 run ros2cs_examples ros2cs_performance_talker
    ros2 run ros2cs_examples ros2cs_performance_listener
    ```
    - windows
    ```
    ros2 run ros2cs_examples ros2cs_performance_talker.exe
    ros2 run ros2cs_examples ros2cs_performance_listener.exe
    ```

## Acknowledgements

The project started as a fork of [ros2_dotnet](https://github.com/ros2-dotnet/ros2_dotnet) but moved away from its root through new features and design choices. Nevertheless, ros2cs is built on foundation of open-source efforts of Esteve Fernandez (esteve), Lennart Nachtigall (firesurfer), Samuel Lindgren (samiamlabs) and other contributors to ros2_dotnet project.

Open-source release of ros2cs was made possible through cooperation with [Tier IV](https://tier4.jp). Thanks to encouragement, support and requirements driven by Tier IV the project was significantly improved in terms of portability, stability, core structure and user-friendliness.
