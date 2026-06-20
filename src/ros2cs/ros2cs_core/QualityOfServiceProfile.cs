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
using System.Threading;

namespace ROS2
{
  /// <summary> Public enum which can be used to acquire predefined qos configurations </summary>
  /// <remarks>
  /// This is mapped to rmw presets, for example SENSOR_DATA is rmw_qos_profile_sensor_data.
  /// Ordinal values intentionally match preset selection in the native wrapper.
  /// </remarks>
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

  /// <summary>History policy values passed directly to rmw_qos_history_policy_t.</summary>
  public enum HistoryPolicy
  {
    /// <summary>Use the middleware default history behavior.</summary>
    QOS_POLICY_HISTORY_SYSTEM_DEFAULT = 0,
    /// <summary>Keep only the last configured number of samples.</summary>
    QOS_POLICY_HISTORY_KEEP_LAST = 1,
    /// <summary>Keep all samples subject to middleware resource limits.</summary>
    QOS_POLICY_HISTORY_KEEP_ALL = 2
  }

  /// <summary>Reliability policy values passed directly to rmw_qos_reliability_policy_t.</summary>
  public enum ReliabilityPolicy
  {
    /// <summary>Use the middleware default reliability behavior.</summary>
    QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT = 0,
    /// <summary>Request reliable delivery.</summary>
    QOS_POLICY_RELIABILITY_RELIABLE = 1,
    /// <summary>Allow best-effort delivery.</summary>
    QOS_POLICY_RELIABILITY_BEST_EFFORT = 2
  }

  /// <summary>Durability policy values passed directly to rmw_qos_durability_policy_t.</summary>
  public enum DurabilityPolicy
  {
    /// <summary>Use the middleware default durability behavior.</summary>
    QOS_POLICY_DURABILITY_SYSTEM_DEFAULT = 0,
    /// <summary>Keep transient local samples for late-joining subscriptions.</summary>
    QOS_POLICY_DURABILITY_TRANSIENT_LOCAL = 1,
    /// <summary>Do not keep samples for late-joining subscriptions.</summary>
    QOS_POLICY_DURABILITY_VOLATILE = 2
  }

  /// <summary>Liveliness policy values passed directly to rmw_qos_liveliness_policy_t.</summary>
  public enum LivelinessPolicy
  {
    /// <summary>Use the middleware default liveliness behavior.</summary>
    QOS_POLICY_LIVELINESS_SYSTEM_DEFAULT = 0,
    /// <summary>Any publisher activity in the node can assert liveliness.</summary>
    QOS_POLICY_LIVELINESS_AUTOMATIC = 1,
    // Ordinal 2 is the deprecated rmw MANUAL_BY_NODE policy; ros2cs does not expose it.
    /// <summary>Each publisher topic is responsible for asserting liveliness.</summary>
    QOS_POLICY_LIVELINESS_MANUAL_BY_TOPIC = 3
  }

  /// <summary> Quality of Service settings for publishers and subscriptions </summary>
  /// <remarks>
  /// Entity constructors copy this profile into native creation options. Mutating a profile after
  /// creating a publisher, subscription, client, or service does not change that existing entity.
  /// </remarks>
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

    /// <summary>Set history policy and depth on this reusable QoS profile.</summary>
    /// <param name="policy">History policy to write into the native rmw profile.</param>
    /// <param name="depth">History depth. KEEP_LAST requires a positive value.</param>
    /// <exception cref="ObjectDisposedException">Thrown after this profile has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="depth"/> is negative or invalid for KEEP_LAST.</exception>
    public void SetHistory(HistoryPolicy policy, int depth)
    {
      lock (mutex)
      {
        ThrowIfDisposed();
        ValidateHistoryDepth(policy, depth);
        NativeRmwInterface.rmw_native_interface_set_history(handle, (int)policy, depth);
      }
    }

    /// <summary>Set reliability policy on this reusable QoS profile.</summary>
    /// <param name="policy">Reliability policy to write into the native rmw profile.</param>
    /// <exception cref="ObjectDisposedException">Thrown after this profile has been disposed.</exception>
    public void SetReliability(ReliabilityPolicy policy)
    {
      lock (mutex)
      {
        ThrowIfDisposed();
        NativeRmwInterface.rmw_native_interface_set_reliability(handle, (int)policy);
      }
    }

    /// <summary>Set durability policy on this reusable QoS profile.</summary>
    /// <param name="policy">Durability policy to write into the native rmw profile.</param>
    /// <exception cref="ObjectDisposedException">Thrown after this profile has been disposed.</exception>
    public void SetDurability(DurabilityPolicy policy)
    {
      lock (mutex)
      {
        ThrowIfDisposed();
        NativeRmwInterface.rmw_native_interface_set_durability(handle, (int)policy);
      }
    }

    /// <summary>Set history, reliability, and durability together under one profile lock.</summary>
    /// <param name="history">History policy to write into the native rmw profile.</param>
    /// <param name="depth">History depth. KEEP_LAST requires a positive value.</param>
    /// <param name="reliability">Reliability policy to write into the native rmw profile.</param>
    /// <param name="durability">Durability policy to write into the native rmw profile.</param>
    /// <exception cref="ObjectDisposedException">Thrown after this profile has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="depth"/> is negative or invalid for KEEP_LAST.</exception>
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

    /// <summary>Set liveliness policy on this reusable QoS profile.</summary>
    /// <param name="policy">Liveliness policy to write into the native rmw profile.</param>
    /// <exception cref="ObjectDisposedException">Thrown after this profile has been disposed.</exception>
    public void SetLiveliness(LivelinessPolicy policy)
    {
      lock (mutex)
      {
        ThrowIfDisposed();
        NativeRmwInterface.rmw_native_interface_set_liveliness(handle, (int)policy);
      }
    }

    public void SetDeadline(TimeSpan deadline)
    {
      ulong nanoseconds = ToNanoseconds(deadline, nameof(deadline));
      lock (mutex)
      {
        ThrowIfDisposed();
        NativeRmwInterface.rmw_native_interface_set_deadline(handle, nanoseconds);
      }
    }

    public void SetLifespan(TimeSpan lifespan)
    {
      ulong nanoseconds = ToNanoseconds(lifespan, nameof(lifespan));
      lock (mutex)
      {
        ThrowIfDisposed();
        NativeRmwInterface.rmw_native_interface_set_lifespan(handle, nanoseconds);
      }
    }

    public void SetLivelinessLeaseDuration(TimeSpan leaseDuration)
    {
      ulong nanoseconds = ToNanoseconds(leaseDuration, nameof(leaseDuration));
      lock (mutex)
      {
        ThrowIfDisposed();
        NativeRmwInterface.rmw_native_interface_set_liveliness_lease_duration(handle, nanoseconds);
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

    internal IntPtr EnterHandleScope()
    {
      Monitor.Enter(mutex);
      try
      {
        ThrowIfDisposed();
        return handle;
      }
      catch
      {
        Monitor.Exit(mutex);
        throw;
      }
    }

    internal void ExitHandleScope()
    {
      Monitor.Exit(mutex);
    }

    /// <summary>Validate history depth before mutating the native rmw profile.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="depth"/> is negative or invalid for KEEP_LAST.</exception>
    private static void ValidateHistoryDepth(HistoryPolicy policy, int depth)
    {
      if (depth < 0)
      {
        throw new ArgumentOutOfRangeException(
          nameof(depth),
          depth,
          "History depth cannot be negative.");
      }

      if (policy == HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST && depth < 1)
      {
        throw new ArgumentOutOfRangeException(
          nameof(depth),
          depth,
          "KEEP_LAST history requires a positive depth.");
      }
    }

    private static ulong ToNanoseconds(TimeSpan value, string paramName)
    {
      if (value < TimeSpan.Zero)
      {
        throw new ArgumentOutOfRangeException(paramName, value, "QoS durations cannot be negative.");
      }

      try
      {
        return checked((ulong)(value.Ticks * 100L));
      }
      catch (OverflowException)
      {
        throw new ArgumentOutOfRangeException(paramName, value, "QoS duration is too large.");
      }
    }
  }

  /// <summary>Owns a temporary preset QoS profile only when the caller did not provide one.</summary>
  internal sealed class QosScope : IDisposable
  {
    private QualityOfServiceProfile ownedProfile;
    private QualityOfServiceProfile scopedProfile;
    private bool scopeLocked;

    internal IntPtr Handle { get; private set; }

    internal QosScope(QualityOfServiceProfile profile, QosPresetProfile defaultPreset)
    {
      QualityOfServiceProfile activeProfile = profile;
      if (activeProfile == null)
      {
        activeProfile = new QualityOfServiceProfile(defaultPreset);
        ownedProfile = activeProfile;
      }

      scopedProfile = activeProfile;
      Handle = scopedProfile.EnterHandleScope();
      scopeLocked = true;
    }

    public void Dispose()
    {
      if (scopeLocked)
      {
        scopeLocked = false;
        scopedProfile.ExitHandleScope();
      }

      if (ownedProfile != null)
      {
        ownedProfile.Dispose();
        ownedProfile = null;
      }
      scopedProfile = null;
      Handle = IntPtr.Zero;
    }
  }
}
