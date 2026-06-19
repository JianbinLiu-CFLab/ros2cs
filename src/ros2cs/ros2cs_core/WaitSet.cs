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
// - Made wait set lifetime disposable.
// - Added disposed-state checks for spin and entity registration.
// - Removed the unused wrapper Clear method; resizing is the active spin path.
// - Removed the unused unbounded wait overload.

using System;

namespace ROS2
{
  internal enum AddResult
  {
    SUCCESS,
    FULL,
    // The wait set or target entity was disposed before it could be registered.
    DISPOSED
  }

  /// <summary>Disposable wrapper around rcl_wait_set_t used by Ros2cs spinning.</summary>
  /// <remarks>
  /// Ros2cs serializes public spin/shutdown paths with its wait-set mutex. This wrapper keeps
  /// its own mutex as a local ownership guard for finalizer cleanup and future internal callers.
  /// </remarks>
  internal class WaitSet : IDisposable
  {
    internal ulong SubscriptionCount {get { return Handle.size_of_subscriptions.ToUInt64(); }}

    internal ulong ClientCount {get { return Handle.size_of_clients.ToUInt64(); }}

    internal ulong ServiceCount {get { return Handle.size_of_services.ToUInt64(); }}

    private rcl_wait_set_t Handle;
    private readonly object mutex = new object();
    private bool disposed;

    internal WaitSet(ref rcl_context_t context, rcl_allocator_t allocator)
    {
      Handle = NativeRcl.rcl_get_zero_initialized_wait_set();
      Utils.CheckReturnEnum(NativeRcl.rcl_wait_set_init(
        ref Handle,
        (UIntPtr)0,
        (UIntPtr)0,
        (UIntPtr)0,
        (UIntPtr)0,
        (UIntPtr)0,
        (UIntPtr)0,
        ref context,
        allocator));
    }

    ~WaitSet()
    {
      Dispose(false);
    }

    /// <summary>Release the native wait set.</summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Shared wait set disposal path used by explicit disposal and the finalizer.</summary>
    private void Dispose(bool disposing)
    {
      lock (mutex)
      {
        // Explicit shutdown disposes this internal type under Ros2cs waitSetMutex; finalizer cleanup is best-effort.
        if (disposed)
        {
          return;
        }

        try
        {
          int ret = NativeRcl.rcl_wait_set_fini(ref Handle);
          if (disposing)
          {
            Utils.CheckReturnEnum(ret);
          }
        }
        catch
        {
          if (disposing)
          {
            throw;
          }
        }
        finally
        {
          disposed = true;
        }
      }
    }

    internal void Resize(ulong subscriptionCount, ulong clientCount, ulong serviceCount)
    {
      lock (mutex)
      {
        ThrowIfDisposed();
        if (SubscriptionCount == subscriptionCount &&
            ClientCount == clientCount &&
            ServiceCount == serviceCount)
        {
          Utils.CheckReturnEnum(NativeRcl.rcl_wait_set_clear(ref Handle));
          return;
        }

        Utils.CheckReturnEnum(NativeRcl.rcl_wait_set_resize(
          ref Handle,
          (UIntPtr)subscriptionCount,
          (UIntPtr)0,
          (UIntPtr)0,
          (UIntPtr)clientCount,
          (UIntPtr)serviceCount,
          (UIntPtr)0));
        Utils.CheckReturnEnum(NativeRcl.rcl_wait_set_clear(ref Handle));
      }
    }

    internal AddResult TryAddSubscription(ISubscriptionBase subscription, out ulong index)
    {
      lock (mutex)
      {
        if (disposed)
        {
          index = default(ulong);
          return AddResult.DISPOSED;
        }

        UIntPtr native_index = default(UIntPtr);
        int ret;
        // Entity locks prevent Dispose from finalizing handles while they are being registered.
        lock (subscription.Mutex)
        {
          if (subscription.IsDisposed)
          {
            index = default(ulong);
            return AddResult.DISPOSED;
          }

          rcl_subscription_t subscription_handle = subscription.Handle;
          ret = NativeRcl.rcl_wait_set_add_subscription(
            ref Handle,
            ref subscription_handle,
            ref native_index
          );
        }

        if ((RCLReturnEnum)ret == RCLReturnEnum.RCL_RET_WAIT_SET_FULL)
        {
          index = default(ulong);
          return AddResult.FULL;
        }
        else
        {
          Utils.CheckReturnEnum(ret);
          index = native_index.ToUInt64();
          return AddResult.SUCCESS;
        }
      }
    }

    internal AddResult TryAddClient(IClientBase client, out ulong index)
    {
      lock (mutex)
      {
        if (disposed)
        {
          index = default(ulong);
          return AddResult.DISPOSED;
        }

        UIntPtr native_index = default(UIntPtr);
        int ret;
        // Entity locks prevent Dispose from finalizing handles while they are being registered.
        lock (client.Mutex)
        {
          if (client.IsDisposed)
          {
            index = default(ulong);
            return AddResult.DISPOSED;
          }

          rcl_client_t client_handle = client.Handle;
          ret = NativeRcl.rcl_wait_set_add_client(
            ref Handle,
            ref client_handle,
            ref native_index
          );
        }

        if ((RCLReturnEnum)ret == RCLReturnEnum.RCL_RET_WAIT_SET_FULL)
        {
          index = default(ulong);
          return AddResult.FULL;
        }
        else
        {
          Utils.CheckReturnEnum(ret);
          index = native_index.ToUInt64();
          return AddResult.SUCCESS;
        }
      }
    }

    internal AddResult TryAddService(IServiceBase service, out ulong index)
    {
      lock (mutex)
      {
        if (disposed)
        {
          index = default(ulong);
          return AddResult.DISPOSED;
        }

        UIntPtr native_index = default(UIntPtr);
        int ret;

        // Entity locks prevent Dispose from finalizing handles while they are being registered.
        lock (service.Mutex)
        {
          if (service.IsDisposed)
          {
            index = default(ulong);
            return AddResult.DISPOSED;
          }

          rcl_service_t service_handle = service.Handle;
          ret = NativeRcl.rcl_wait_set_add_service(
            ref Handle,
            ref service_handle,
            ref native_index
          );
        }

        if ((RCLReturnEnum)ret == RCLReturnEnum.RCL_RET_WAIT_SET_FULL)
        {
          index = default(ulong);
          return AddResult.FULL;
        }
        else
        {
          Utils.CheckReturnEnum(ret);
          index = native_index.ToUInt64();
          return AddResult.SUCCESS;
        }
      }
    }

    internal bool Wait(TimeSpan timeout)
    {
      lock (mutex)
      {
        ThrowIfDisposed();
        int ret = NativeRcl.rcl_wait(ref Handle, ToNanoseconds(timeout));
        if ((RCLReturnEnum)ret == RCLReturnEnum.RCL_RET_TIMEOUT)
        {
          return false;
        }
        else
        {
          Utils.CheckReturnEnum(ret);
          return true;
        }
      }
    }

    /// <summary>Convert managed timeouts to rcl nanoseconds without silent overflow.</summary>
    internal static long ToNanoseconds(TimeSpan timeout)
    {
      return checked(timeout.Ticks * 100L);
    }

    /// <summary>Reject wait set operations after shutdown has released the native handle.</summary>
    private void ThrowIfDisposed()
    {
      if (disposed)
      {
        throw new ObjectDisposedException(nameof(WaitSet));
      }
    }
  }
}
