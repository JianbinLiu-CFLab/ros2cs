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
// - Added node-owned disposal path and options cleanup.
// - Added safe message disposal and take-failure handling.
// - Propagates native subscription option finalization failures during explicit disposal.
// - Made disposal state volatile for node/entity visibility.

using System;
using System.Collections.Generic;
using ROS2.Internal;

namespace ROS2
{
  /// <summary> Subscription to a topic with a given type and node-owned native lifetime. </summary>
  /// <description> Subscriptions are created through INode interface (CreateSubscription) </description>
  public class Subscription<T>: ISubscription<T>, INodeChildEntity where T : Message, new ()
  {
    public rcl_subscription_t Handle { get { return subscriptionHandle; } }
    private rcl_subscription_t subscriptionHandle;

    public string Topic { get { return topic; } }
    private string topic;

    public bool IsDisposed { get { return disposed; } }
    private volatile bool disposed = false;

    // Keep the owning node reference so fini calls always use the current native node handle.
    private readonly Node node;
    private readonly Action<T> callback;
    // Native subscription options are released with the subscription, including constructor failure paths.
    private IntPtr subscriptionOptions = IntPtr.Zero;

    public object Mutex { get { return mutex; } }
    private object mutex = new object();

    /// <summary> Tries to get a message from rcl/rmw layers. Calls the callback if successful </summary>
    // Internal spin entry point; kept public through ISubscriptionBase for compatibility.
    public void TakeMessage()
    {
      MessageInternals message = null;
      RCLReturnEnum ret;

      lock (mutex)
      {
        if (disposed || !Ros2cs.Ok())
        {
          return;
        }

        message = CreateMessage();
        ret = (RCLReturnEnum)NativeRcl.rcl_take(ref subscriptionHandle, message.Handle, IntPtr.Zero, IntPtr.Zero);
      }

      if (ret == RCLReturnEnum.RCL_RET_SUBSCRIPTION_TAKE_FAILED)
      {
        // No message was available after wait; dispose the temporary wrapper quietly.
        ((IDisposable)message).Dispose();
        return;
      }

      if (ret != RCLReturnEnum.RCL_RET_OK)
      {
        ((IDisposable)message).Dispose();
        Utils.CheckReturnEnum((int)ret);
        return;
      }

      try
      {
        TriggerCallback(message);
      }
      finally
      {
        ((IDisposable)message).Dispose();
      }
    }

    /// <summary> Construct a message of the subscription type and validate its native-message interface. </summary>
    private MessageInternals CreateMessage()
    {
      T msg = new T();
      try
      {
        return MessageTypeSupportHelper.AsMessageInternals(msg, nameof(msg));
      }
      catch
      {
        msg.Dispose();
        throw;
      }
    }

    /// <summary> Populates managed fields with native values and calls the callback with created message </summary>
    /// <param name="message"> Message that will be populated and returned through callback </param>
    private void TriggerCallback(MessageInternals message)
    {
      message.ReadNativeMessage();
      callback((T)message);
    }

    /// <summary> Internal constructor for Subscription. Use INode.CreateSubscription to construct </summary>
    /// <see cref="INode.CreateSubscription"/>
    internal Subscription(string subTopic, Node node, Action<T> cb, QualityOfServiceProfile qos = null)
    {
      callback = cb;
      this.node = node;
      topic = subTopic;
      subscriptionHandle = NativeRcl.rcl_get_zero_initialized_subscription();

      QualityOfServiceProfile qualityOfServiceProfile = qos;
      bool ownsQos = false;
      if (qualityOfServiceProfile == null)
      {
        qualityOfServiceProfile = new QualityOfServiceProfile();
        ownsQos = true;
      }

      try
      {
        subscriptionOptions = NativeRclInterface.rclcs_subscription_create_options(qualityOfServiceProfile.Handle);
        if (subscriptionOptions == IntPtr.Zero)
        {
          throw new RuntimeError("Failed to create subscription options");
        }
      }
      finally
      {
        if (ownsQos)
        {
          qualityOfServiceProfile.Dispose();
        }
      }

      IntPtr typeSupportHandle = MessageTypeSupportHelper.GetTypeSupportHandle<T>();

      try
      {
        Utils.CheckReturnEnum(NativeRcl.rcl_subscription_init(
          ref subscriptionHandle,
          ref node.nodeHandle,
          typeSupportHandle,
          topic,
          subscriptionOptions));
      }
      catch (Exception initException)
      {
        List<Exception> exceptions = new List<Exception> { initException };
        try
        {
          Utils.CheckReturnEnum(NativeRclInterface.rclcs_subscription_dispose_options(subscriptionOptions));
        }
        catch (Exception disposeException)
        {
          exceptions.Add(disposeException);
        }
        finally
        {
          subscriptionOptions = IntPtr.Zero;
        }
        Utils.ThrowCollectedExceptions(exceptions);
        throw;
      }
    }

    ~Subscription()
    {
      Dispose(false);
    }

    /// <summary>Release the subscription and its native options.</summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Release this subscription during node disposal without re-entering node removal.</summary>
    void INodeChildEntity.DisposeFromNode(bool disposing)
    {
      Dispose(disposing);
      if (disposing)
      {
        GC.SuppressFinalize(this);
      }
    }

    /// <summary>Shared subscription disposal path used by explicit disposal, node disposal, and finalization.</summary>
    private void Dispose(bool disposing)
    {
      List<Exception> disposeExceptions = null;
      lock (mutex)
      {
        if (disposed)
        {
          return;
        }

        try
        {
          if (!node.IsDisposed)
          {
            int ret = NativeRcl.rcl_subscription_fini(ref subscriptionHandle, ref node.nodeHandle);
            if (disposing)
            {
              Utils.CheckReturnEnum(ret);
            }
          }
        }
        catch (Exception e)
        {
          if (disposing)
          {
            Utils.AddException(ref disposeExceptions, e);
          }
        }
        finally
        {
          try
          {
            if (subscriptionOptions != IntPtr.Zero)
            {
              Utils.CheckReturnEnum(NativeRclInterface.rclcs_subscription_dispose_options(subscriptionOptions));
            }
          }
          catch (Exception e)
          {
            if (disposing)
            {
              Utils.AddException(ref disposeExceptions, e);
            }
          }
          finally
          {
            subscriptionOptions = IntPtr.Zero;
            disposed = true;
          }
        }
      }

      if (disposing)
      {
        Ros2csLogger.GetInstance().LogInfo("Subscription destroyed");
        Utils.ThrowCollectedExceptions(disposeExceptions);
      }
    }
  }
}
