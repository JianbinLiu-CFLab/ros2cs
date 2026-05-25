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
// - Retained ros2cs native library handle while wrapper delegates are in use.
// - Added calling convention metadata for rclcs init delegate.
// - Corrected internal default-node-options delegate spelling.
// - Shared the ros2cs native wrapper handle with rmw wrapper bindings.

using System;
using System.Runtime.InteropServices;

namespace ROS2
{
  /// <summary>Shared owner for the ros2cs native wrapper library.</summary>
  internal static class NativeRos2csLibrary
  {
    internal static readonly DllLoadUtils DllLoadUtils = DllLoadUtilsFactory.GetDllLoadUtils();
    internal static readonly NativeLibraryHandle Handle = NativeLibraryHandle.LoadLibrary(DllLoadUtils, "ros2cs");
    internal static readonly IntPtr Ptr = Handle.Handle;
  }

  /// <summary>
  /// An internal class to manage all wrapped native calls to rcl and rcutils.
  /// These are wrapped usually for one of two reasons:
  /// 1. Packaging several rcl calls into one (e. g. rclcs_init)
  /// 2. Tracking / handling of unmanaged memory that is allocated in the native library
  /// </summary>
  internal static class NativeRclInterface
  {
    private static readonly DllLoadUtils dllLoadUtils = NativeRos2csLibrary.DllLoadUtils;
    private static readonly IntPtr nativeROS2CS = NativeRos2csLibrary.Ptr;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int RCLCSInitType(ref rcl_context_t context, rcl_allocator_t allocator);
    internal static RCLCSInitType
        rclcs_init =
        (RCLCSInitType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_init"),
        typeof(RCLCSInitType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr GetErrorStringType();
    internal static GetErrorStringType
        rclcs_get_error_string =
        (GetErrorStringType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_get_error_string"),
        typeof(GetErrorStringType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void DisposeErrorStringType(IntPtr error_c_string);
    internal static DisposeErrorStringType
        rclcs_dispose_error_string =
        (DisposeErrorStringType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_dispose_error_string"),
        typeof(DisposeErrorStringType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr NodeCreateDefaultOptionsType();
    internal static NodeCreateDefaultOptionsType
        rclcs_node_create_default_options =
        (NodeCreateDefaultOptionsType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_node_create_default_options"),
        typeof(NodeCreateDefaultOptionsType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void NodeDisposeOptionsType(IntPtr options);
    internal static NodeDisposeOptionsType
        rclcs_node_dispose_options =
        (NodeDisposeOptionsType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_node_dispose_options"),
        typeof(NodeDisposeOptionsType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr SubscriptionCreateOptionsType(IntPtr qos);
    internal static SubscriptionCreateOptionsType
        rclcs_subscription_create_options =
        (SubscriptionCreateOptionsType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_subscription_create_options"),
        typeof(SubscriptionCreateOptionsType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SubscriptionDisposeOptionsType(IntPtr options);
    internal static SubscriptionDisposeOptionsType
        rclcs_subscription_dispose_options =
        (SubscriptionDisposeOptionsType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_subscription_dispose_options"),
        typeof(SubscriptionDisposeOptionsType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr PublisherCreateOptionsType(IntPtr qos);
    internal static PublisherCreateOptionsType
        rclcs_publisher_create_options =
        (PublisherCreateOptionsType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_publisher_create_options"),
        typeof(PublisherCreateOptionsType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void PublisherDisposeOptionsType(IntPtr options);
    internal static PublisherDisposeOptionsType
        rclcs_publisher_dispose_options =
        (PublisherDisposeOptionsType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_publisher_dispose_options"),
        typeof(PublisherDisposeOptionsType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ClientCreateOptionsType(IntPtr qos);
    internal static ClientCreateOptionsType
        rclcs_client_create_options =
        (ClientCreateOptionsType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_client_create_options"),
        typeof(ClientCreateOptionsType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ClientDisposeOptionsType(IntPtr options);
    internal static ClientDisposeOptionsType
        rclcs_client_dispose_options =
        (ClientDisposeOptionsType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_client_dispose_options"),
        typeof(ClientDisposeOptionsType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ServiceCreateOptionsType(IntPtr qos);
    internal static ServiceCreateOptionsType
        rclcs_service_create_options =
        (ServiceCreateOptionsType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_service_create_options"),
        typeof(ServiceCreateOptionsType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ServiceDisposeOptionsType(IntPtr options);
    internal static ServiceDisposeOptionsType
        rclcs_service_dispose_options =
        (ServiceDisposeOptionsType)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_service_dispose_options"),
        typeof(ServiceDisposeOptionsType));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr RclcsClockCreate(ref rcl_allocator_t allocator_handle);
    internal static RclcsClockCreate
        rclcs_ros_clock_create =
        (RclcsClockCreate)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_ros_clock_create"),
        typeof(RclcsClockCreate));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void RclcsClockDispose(IntPtr clock_handle);
    internal static RclcsClockDispose
        rclcs_ros_clock_dispose =
        (RclcsClockDispose)Marshal.GetDelegateForFunctionPointer(dllLoadUtils.GetProcAddress(
        nativeROS2CS,
        "rclcs_ros_clock_dispose"),
        typeof(RclcsClockDispose));
  }
}
