// Copyright 2019-2021 Robotec.ai
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
// - Split rcl exception throwing so callers can inspect/filter native error messages.
// - Ensured native error strings are released even if managed marshaling fails.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ROS2
{
  /// <summary> Internal utilities for ros2cs_core </summary>
  internal static class Utils
  {
    /// <summary> Helper checker and converter of rcl return values to exceptions </summary>
    internal static void CheckReturnEnum(int ret)
    {
      if ((RCLReturnEnum)ret == RCLReturnEnum.RCL_RET_OK)
      {
        return;
      }

      string errorMessage = Utils.PopRclErrorString();
      ThrowRclException(ret, errorMessage);
    }

    /// <summary>Throw a typed managed exception for an rcl return code and supplied error text.</summary>
    internal static void ThrowRclException(int ret, string errorMessage)
    {
      switch ((RCLReturnEnum)ret)
      {
        case RCLReturnEnum.RCL_RET_NODE_INVALID_NAME:
          throw new InvalidNodeNameException(errorMessage);
        case RCLReturnEnum.RCL_RET_NODE_INVALID_NAMESPACE:
          throw new InvalidNamespaceException(errorMessage);
        case RCLReturnEnum.RCL_RET_WAIT_SET_EMPTY:
          throw new WaitSetEmptyException(errorMessage);
        default:
          throw new RuntimeError(errorMessage, ret);
      }
    }

    /// <summary>Add an exception to a lazily-created collection.</summary>
    internal static void AddException(ref List<Exception> exceptions, Exception exception)
    {
      if (exceptions == null)
      {
        exceptions = new List<Exception>();
      }
      exceptions.Add(exception);
    }

    /// <summary>Throw one collected exception directly or aggregate multiple failures.</summary>
    internal static void ThrowCollectedExceptions(List<Exception> exceptions)
    {
      if (exceptions == null || exceptions.Count == 0)
      {
        return;
      }
      if (exceptions.Count == 1)
      {
        throw exceptions[0];
      }
      throw new AggregateException(exceptions);
    }

    /// <summary> Get last rcl error </summary>
    /// <returns> String with error message </returns>
    internal static string GetRclErrorString()
    {
      IntPtr errorStringPtr = NativeRclInterface.rclcs_get_error_string();
      try
      {
        return PtrToString(errorStringPtr);
      }
      finally
      {
        if (errorStringPtr != IntPtr.Zero)
        {
          NativeRclInterface.rclcs_dispose_error_string(errorStringPtr);
        }
      }
    }

    /// <summary> Get and clean last rcl error </summary>
    /// <returns> String with error message </returns>
    internal static string PopRclErrorString()
    {
      string errorString = GetRclErrorString();
      NativeRcl.rcl_reset_error();
      return errorString;
    }

    /// <summary> Marshal a pointer to string </summary>
    /// <returns> String or null if the pointer was Zero </returns>
    internal static string PtrToString(IntPtr p)
    {
      if (p == IntPtr.Zero)
      {
        return null;
      }
      return Marshal.PtrToStringAnsi(p);
    }
  }
}
