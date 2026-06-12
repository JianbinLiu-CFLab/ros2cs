# ROS2CS - Ubuntu 20.04 / 22.04 / 24.04 (legacy - not recently verified)

> Modifications Copyright (c) 2026 Jianbin Liu.
>
> Modifications by Jianbin Liu:
> - Marked Ubuntu instructions as legacy / not recently verified for the current Jazzy maintenance branch.
> - Noted the .NET 8 tests/examples target framework change without rewriting the Ubuntu flow.
> - Refreshed SDK and ROS sourcing examples to avoid stale .NET 6 / Foxy copy-paste paths.

## Current verification status

These Ubuntu instructions are legacy guidance. They have not been recently verified for the current Jazzy maintenance branch.

Current local validation was performed on Windows 10 LTSC with ROS 2 Jazzy. Ubuntu 20.04 / 22.04 / 24.04 should be treated as unverified until a fresh Ubuntu build/test run records commands, exit codes, and key output.

The current target framework split is:

- `ros2cs_common`, `ros2cs_core`, and generated message assemblies: `netstandard2.0`.
- `ros2cs_tests` and `ros2cs_examples`: `net8.0`.

The Ubuntu flow below still needs a fresh build/test run before claiming support
for this branch, but the SDK and ROS sourcing commands now match the current
target framework split.

## Building

### Prerequisites

**General**

- ROS2 installed on the system, along with `test-msgs`, `cyclonedds` and `fastrtps` packages
- vcstool package - [see here](https://github.com/dirk-thomas/vcstool)
- .NET 8 SDK - [see here](https://www.microsoft.com/net/learn/get-started)


```bash
# Install rmw and tests-msgs for your ROS2 distribution
apt install -y ros-${ROS_DISTRO}-test-msgs
apt install -y ros-${ROS_DISTRO}-fastrtps ros-${ROS_DISTRO}-rmw-fastrtps-cpp
apt install -y ros-${ROS_DISTRO}-cyclonedds ros-${ROS_DISTRO}-rmw-cyclonedds-cpp

# Install vcstool package
curl -s https://packagecloud.io/install/repositories/dirk-thomas/vcstool/script.deb.sh | sudo bash
sudo apt-get update
sudo apt-get install -y python3-vcstool

# Install Microsoft packages (Ubuntu 20.04 only)
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Install .NET 8 SDK
sudo apt-get update; \
  sudo apt-get install -y apt-transport-https && \
  sudo apt-get update && \
  sudo apt-get install -y dotnet-sdk-8.0
```

**Optional**

- `patchelf` tool for standalone version builds

```bash
sudo apt install patchelf
```

### Steps

- Clone this project
- Source your ROS2 installation
  ```bash
  source /opt/ros/${ROS_DISTRO}/setup.bash
  ```
- Navigate to the top project folder and pull required repositories
  ```bash
  ./get_repos.sh
  ```
  - You can run `get_repos` script with `--get-custom-messages argument` to fetch extra messages from `custom_messages.repos` file.
  - It will use `vcstool` to download required ROS2 packages. By default, this will get repositories as set in `ros2_${ROS_DISTRO}.repos`.
- Build package in _overlay_ mode:
  ```bash
  ./build.sh
  ```
  or to build a _standalone_ version:
  ```bash
  ./build.sh --standalone
  ```
  - It invokes `colcon_build` with `--merge-install` argument to simplify libraries installation.
  - You can build tests by adding `--with-tests` argument to command.
- To test your build please check main readme [Testing section](README.md#testing)
