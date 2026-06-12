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
// - Made QoS profiles disposable.
// - Added native handle validation before QoS mutation.
// - Added QoS policy validation, explicit rmw ordinals, and liveliness setter support.

using System;

namespace ROS2
{
  /// <summary> Public enum which can be used to acquire predefined qos configurations </summary>
  /// <remarks> This is mapped to rmw presets, for example SENSOR_DATA is rmw_qos_profile_sensor_data </remarks>
  public enum QosPresetProfile
  {
    SENSOR_DATA = 0,
    PARAMETERS = 1,
    DEFAULT = 2,
    SERVICES_DEFAULT = 3,
    PARAMETER_EVENTS = 4,
    /// <summary>Vendor/system-defined policies; prefer an explicit preset for portable behavior.</summary>
    SYSTEM_DEFAULT = 5
  }

  public enum HistoryPolicy
  {
    QOS_POLICY_HISTORY_SYSTEM_DEFAULT = 0,
    QOS_POLICY_HISTORY_KEEP_LAST = 1,
    QOS_POLICY_HISTORY_KEEP_ALL = 2
  }

  public enum ReliabilityPolicy
  {
    QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT = 0,
    QOS_POLICY_RELIABILITY_RELIABLE = 1,
    QOS_POLICY_RELIABILITY_BEST_EFFORT = 2
  }

  public enum DurabilityPolicy
  {
    QOS_POLICY_DURABILITY_SYSTEM_DEFAULT = 0,
    QOS_POLICY_DURABILITY_TRANSIENT_LOCAL = 1,
    QOS_POLICY_DURABILITY_VOLATILE = 2
  }

  public enum LivelinessPolicy
  {
    QOS_POLICY_LIVELINESS_SYSTEM_DEFAULT = 0,
    QOS_POLICY_LIVELINESS_AUTOMATIC = 1,
    QOS_POLICY_LIVELINESS_MANUAL_BY_TOPIC = 3
  }

  /// <summary> Quality of Service settings for publishers and subscriptions </summary>
  public class QualityOfServiceProfile : IDisposable
  {
    // Native rmw_qos_profile_t wrapper owned by this managed object.
    internal IntPtr handle;
    private readonly object mutex = new object();
    private bool disposed;

    /// <summary>Native QoS profile handle, guarded against use after disposal.</summary>
    internal IntPtr Handle
    {
      get
      {
        lock (mutex)
        {
          ThrowIfDisposed();
          return handle;
        }
      }
    }

    /// <summary> Construct using a preset </summary>
    public QualityOfServiceProfile(QosPresetProfile preset_profile = QosPresetProfile.DEFAULT)
    {
      handle = NativeRmwInterface.rmw_native_interface_create_qos_profile((int)preset_profile);
      if (handle == IntPtr.Zero)
      {
        throw new RuntimeError("Failed to create QoS profile");
      }
    }

    public void SetHistory(HistoryPolicy policy, int depth)
    {
      lock (mutex)
      {
        ThrowIfDisposed();
        ValidateHistoryDepth(policy, depth);
        NativeRmwInterface.rmw_native_interface_set_history(handle, (int)policy, depth);
      }
    }

    public void SetReliability(ReliabilityPolicy policy)
    {
      lock (mutex)
      {
        ThrowIfDisposed();
        NativeRmwInterface.rmw_native_interface_set_reliability(handle, (int)policy);
      }
    }

    public void SetDurability(DurabilityPolicy policy)
    {
      lock (mutex)
      {
        ThrowIfDisposed();
        NativeRmwInterface.rmw_native_interface_set_durability(handle, (int)policy);
      }
    }

    public void SetPolicies(
      HistoryPolicy history,
      int depth,
      ReliabilityPolicy reliability,
      DurabilityPolicy durability)
    {
      lock (mutex)
      {
        ThrowIfDisposed();
        ValidateHistoryDepth(history, depth);
        NativeRmwInterface.rmw_native_interface_set_history(handle, (int)history, depth);
        NativeRmwInterface.rmw_native_interface_set_reliability(handle, (int)reliability);
        NativeRmwInterface.rmw_native_interface_set_durability(handle, (int)durability);
      }
    }

    public void SetLiveliness(LivelinessPolicy policy)
    {
      lock (mutex)
      {
        ThrowIfDisposed();
        NativeRmwInterface.rmw_native_interface_set_liveliness(handle, (int)policy);
      }
    }

    /// <summary>Release the native QoS profile wrapper.</summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    ~QualityOfServiceProfile()
    {
      Dispose(false);
    }

    /// <summary>Shared QoS disposal path used by explicit disposal and the finalizer.</summary>
    private void Dispose(bool disposing)
    {
      lock (mutex)
      {
        Exception disposeException = null;
        if (disposed)
        {
          return;
        }

        try
        {
          if (handle != IntPtr.Zero)
          {
            NativeRmwInterface.rmw_native_interface_delete_qos_profile(handle);
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

        if (disposeException != null)
        {
          throw disposeException;
        }
      }
    }

    /// <summary>Reject mutations after the native QoS profile has been released.</summary>
    private void ThrowIfDisposed()
    {
      if (disposed)
      {
        throw new ObjectDisposedException(nameof(QualityOfServiceProfile));
      }
    }

    private static void ValidateHistoryDepth(HistoryPolicy policy, int depth)
    {
      if (policy == HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST && depth < 1)
      {
        throw new ArgumentOutOfRangeException(
          nameof(depth),
          depth,
          "KEEP_LAST history requires a positive depth.");
      }
    }
  }

  /// <summary>Owns a temporary preset QoS profile only when the caller did not provide one.</summary>
  internal sealed class QosScope : IDisposable
  {
    private QualityOfServiceProfile ownedProfile;

    internal IntPtr Handle { get; private set; }

    internal QosScope(QualityOfServiceProfile profile, QosPresetProfile defaultPreset)
    {
      QualityOfServiceProfile activeProfile = profile;
      if (activeProfile == null)
      {
        activeProfile = new QualityOfServiceProfile(defaultPreset);
        ownedProfile = activeProfile;
      }

      Handle = activeProfile.Handle;
    }

    public void Dispose()
    {
      if (ownedProfile != null)
      {
        ownedProfile.Dispose();
        ownedProfile = null;
      }
      Handle = IntPtr.Zero;
    }
  }
}
