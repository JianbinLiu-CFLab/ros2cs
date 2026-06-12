// Copyright 2019-2021 Robotec.ai
// Copyright 2019 Dyno Robotics (by Samuel Lindgren samuel@dynorobotics.se)
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// Modifications by Jianbin Liu:
// - Added nanosecond normalization helper for negative time values.
// - Added safe clock creation validation and disposal handling.
// - Documented context-independent ROS clock behavior.

using System;
using System.Collections.Generic;

namespace ROS2
{
  /// <summary> A simple structure to hold seconds and nanoseconds </summary>
  /// <description> This is meant to be an intermediate data object before time is packed into
  /// a rosgraph clock message or into a different format native to application layer </description>
  public struct RosTime
  {
    public int sec;
    public uint nanosec;

    public double Seconds
    {
      get { return sec + nanosec/1e9; }
    }
  }

  /// <summary> A clock class which queries an internal rcl clock and exposes RosTime </summary>
  /// <remarks>
  /// Clock owns a standalone rcl_clock_t and is not registered with the global Ros2cs context.
  /// It can continue returning time across Ros2cs Shutdown/Init cycles until disposed. The ROS clock
  /// uses ROS 2 clock semantics: without an active simulation clock source, it falls back to system time.
  /// </remarks>
  public class Clock : IExtendedDisposable
  {
    /// <summary>Number of nanoseconds in one second, used for lossless normalization.</summary>
    private const long NanosecondsPerSecond = 1000000000L;
    // Serializes native clock reads with disposal/finalizer cleanup.
    private readonly object mutex = new object();
    internal IntPtr handle;
    private bool disposed;

    public bool IsDisposed { get { return disposed; } }

    /// <summary> Query current time </summary>
    /// <returns> Time in full seconds and nanoseconds </returns>
    public RosTime Now
    {
      get
      {
        lock (mutex)
        {
          if (disposed)
          {
            throw new ObjectDisposedException(nameof(Clock));
          }

          long queryNowNanoseconds = 0;
          Utils.CheckReturnEnum(NativeRcl.rcl_clock_get_now(handle, ref queryNowNanoseconds));
          return FromNanoseconds(queryNowNanoseconds);
        }
      }
    }

    /// <summary>Normalize signed nanoseconds into ROS seconds plus non-negative nanoseconds.</summary>
    internal static RosTime FromNanoseconds(long nanosecondsSinceEpoch)
    {
      long seconds = nanosecondsSinceEpoch / NanosecondsPerSecond;
      long nanoseconds = nanosecondsSinceEpoch % NanosecondsPerSecond;
      if (nanoseconds < 0)
      {
        seconds--;
        nanoseconds += NanosecondsPerSecond;
      }

      if (seconds < int.MinValue || seconds > int.MaxValue)
      {
        throw new OverflowException("ROS time seconds exceed the int32 range of builtin_interfaces/msg/Time");
      }

      RosTime time = new RosTime();
      time.sec = (int)seconds;
      time.nanosec = (uint)nanoseconds;
      return time;
    }

    public Clock()
    {
      rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
      handle = NativeRclInterface.rclcs_ros_clock_create(ref allocator);
      if (handle == IntPtr.Zero)
      {
        throw new RuntimeError("Failed to create ROS clock");
      }
    }

    ~Clock()
    {
      Dispose(false);
    }

    /// <summary>Release the native ROS clock wrapper.</summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Shared clock disposal path used by explicit disposal and the finalizer.</summary>
    private void Dispose(bool disposing)
    {
      Exception disposeException = null;
      lock (mutex)
      {
        if (disposed)
        {
          return;
        }

        try
        {
          if (handle != IntPtr.Zero)
          {
            NativeRclInterface.rclcs_ros_clock_dispose(handle);
          }
        }
        catch (Exception e)
        {
          if (disposing)
          {
            disposeException = e;
          }
        }
        finally
        {
          handle = IntPtr.Zero;
          disposed = true;
        }
      }

      if (disposeException != null)
      {
        throw disposeException;
      }
    }
  }
}
