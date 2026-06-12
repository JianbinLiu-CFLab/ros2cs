# Ros2Cs Examples

> Modifications Copyright (c) 2026 Jianbin Liu.
>
> Modifications by Jianbin Liu:
> - Corrected example launch commands and PointCloud2 wording.
> - Documented that examples target .NET 8 in the current maintenance branch.
> - Documented performance example QoS compatibility.

Current maintenance branch note: `ros2cs_examples` targets `net8.0`. Core/common/generated ros2cs assemblies remain `netstandard2.0`.

## Examples

*  `ROS2Listener` / `ROS2Talker` - simple string subscriber/publisher test.
*  `ROS2PerformanceListener` / `ROS2PerformanceTalker` - performance test using PointCloud2 data.
*  `ROS2Service` / `ROS2Client` - simple `AddTwoInts` service/client test.

## Simple subscriber/listener

1.  Build project:
 
    ```bash
    ./build.sh
    ```

2.  Run listener:
  
    ```bash
    ros2 run ros2cs_examples ros2cs_listener
    ```

3.  Run talker:

    ```bash
    ros2 run ros2cs_examples ros2cs_talker
    ```

    On Windows, use the `.exe` entry points:

    ```powershell
    ros2 run ros2cs_examples ros2cs_listener.exe
    ros2 run ros2cs_examples ros2cs_talker.exe
    ```

Listener will print out `"I heard: [Hello World: X]` messages sent by a talker.

## Simple service/client

1. Build project:

    ```bash
    ./build.sh
    ```

2. Run service:

    ```bash
    ros2 run ros2cs_examples ros2cs_service
    ```

3. Run client:

    ```bash
    ros2 run ros2cs_examples ros2cs_client
    ```

    On Windows, use the `.exe` entry points:

    ```powershell
    ros2 run ros2cs_examples ros2cs_service.exe
    ros2 run ros2cs_examples ros2cs_client.exe
    ```

The service prints the incoming `A` and `B` values. The client sends `7 + 2` and prints `Sum = 9`.

## Performance test

The performance talker/listener use `QosPresetProfile.SENSOR_DATA`, which maps to BEST_EFFORT
reliability for high-rate data. ROS 2 Jazzy `ros2 topic echo` adapts to compatible publisher QoS,
but explicit RELIABLE subscribers or older tools must request BEST_EFFORT to receive this topic.

1.  Build project:
 
    ```bash
    ./build.sh
    ```

2.  Run talker:
  
    ```bash
    ros2 run ros2cs_examples ros2cs_performance_talker
    ```

3.  When asked, set desired `PointCloud2` data size (number of points),

4.  Run listener:

    ```bash
    ros2 run ros2cs_examples ros2cs_performance_listener
    ```

    On Windows, use the `.exe` entry points:

    ```powershell
    ros2 run ros2cs_examples ros2cs_performance_talker.exe
    ros2 run ros2cs_examples ros2cs_performance_listener.exe
    ```

5. When asked, set desired sample size (number of messages).

After receiving the desired number of samples, listener will print out average latency and its `Latency of sample size X - avg: Ys, std dev: Zs`

### Example results

Hardware spec:
*  **CPU**: i7 4970k
*  **MEM**: 16GB Ram

| PointCloud size | Sample size | Average rate [Hz] | Average latency [s] | Latency std dev [s] |
|-|-|-|-|-|
| 100 000 | 5000 | 719.308 | 0.001591 | 0.000306 |
| 1 000 000 | 500 | 77.025 | 0.022677 | 0.001607 |
