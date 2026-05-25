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
// - Suppressed stale client response noise during shutdown/reconnect.
// - Added safe request message disposal.

using System;
using System.Collections.Generic;
using ROS2.Internal;

namespace ROS2
{
    /// <summary>Service with a topic, message types, and node-owned native lifetime.</summary>
    /// <remarks>Instances are created by <see cref="INode.CreateService"/></remarks>
    /// <typeparam name="I">Message Type to be received</typeparam>
    /// <typeparam name="O">Message Type to be send</typeparam>
    public class Service<I, O>: IService<I, O>, INodeChildEntity
    where I : Message, new ()
    where O : Message, new ()
  {
    public rcl_service_t Handle { get { return serviceHandle; } }
    private rcl_service_t serviceHandle;

    /// <summary>
    /// Topic of this Service
    /// </summary>
    public string Topic { get { return topic; } }
    private string topic;

    /// <inheritdoc/>
    public bool IsDisposed { get { return disposed; } }
    private bool disposed = false;

    // Keep the owning node reference so fini calls always use the current native node handle.
    private readonly Node node;

    /// <summary>
    /// Callback to be called to process incoming requests
    /// </summary>
    private readonly Func<I, O> callback;
    // Native service options are released with the service, including constructor failure paths.
    private IntPtr serviceOptions = IntPtr.Zero;

    /// <inheritdoc/>
    public object Mutex { get { return mutex; } }
    private object mutex = new object();

    /// <summary>
    /// Internal constructor for Service
    /// </summary>
    /// <remarks>Use <see cref="INode.CreateService"/> to construct new Instances</remarks>
    internal Service(string subTopic, Node node, Func<I, O> cb, QualityOfServiceProfile qos = null)
    {
      callback = cb;
      this.node = node;
      topic = subTopic;
      serviceHandle = NativeRcl.rcl_get_zero_initialized_service();

      QualityOfServiceProfile qualityOfServiceProfile = qos;
      bool ownsQos = false;
      if (qualityOfServiceProfile == null)
      {
        qualityOfServiceProfile = new QualityOfServiceProfile(QosPresetProfile.SERVICES_DEFAULT);
        ownsQos = true;
      }

      try
      {
        serviceOptions = NativeRclInterface.rclcs_service_create_options(qualityOfServiceProfile.Handle);
        if (serviceOptions == IntPtr.Zero)
        {
          throw new RuntimeError("Failed to create service options");
        }
      }
      finally
      {
        if (ownsQos)
        {
          qualityOfServiceProfile.Dispose();
        }
      }

      IntPtr typeSupportHandle = MessageTypeSupportHelper.GetTypeSupportHandle<I>();

      try
      {
        Utils.CheckReturnEnum(NativeRcl.rcl_service_init(
          ref serviceHandle,
          ref node.nodeHandle,
          typeSupportHandle,
          topic,
          serviceOptions));
      }
      catch
      {
        NativeRclInterface.rclcs_service_dispose_options(serviceOptions);
        serviceOptions = IntPtr.Zero;
        throw;
      }
    }

    /// <summary>
    /// Send Response Message with rcl/rmw layers
    /// </summary>
    /// <param name="header">request id received when taking the Request</param>
    /// <param name="msg">Message to be send</param>
    private void SendResp(rcl_rmw_request_id_t header, O msg)
    {
      MessageInternals msgInternals = MessageTypeSupportHelper.AsMessageInternals(msg, nameof(msg));
      msgInternals.WriteNativeMessage();
      int ret = NativeRcl.rcl_send_response(ref serviceHandle, ref header, msgInternals.Handle);
      if ((RCLReturnEnum)ret == RCLReturnEnum.RCL_RET_OK)
      {
        return;
      }

      string errorMessage = Utils.PopRclErrorString();
      if (errorMessage != null &&
          errorMessage.IndexOf("client will not receive response", StringComparison.Ordinal) >= 0)
      {
        // Late responses can occur during reconnect/shutdown; rcl reports them even though no fix is needed.
        return;
      }

      Utils.ThrowRclException(ret, errorMessage);
    }

    /// <inheritdoc/>
    // Internal spin entry point; kept public through IServiceBase for compatibility.
    public void TakeMessage()
    {
      RCLReturnEnum ret;
      rcl_rmw_request_id_t header = default(rcl_rmw_request_id_t);
      MessageInternals message = null;

      lock (mutex)
      {
        if (disposed || !Ros2cs.Ok())
        {
          return;
        }
        message = CreateMessage();

        ret = (RCLReturnEnum)NativeRcl.rcl_take_request(ref serviceHandle, ref header, message.Handle);
      }

      if (ret == RCLReturnEnum.RCL_RET_SERVICE_TAKE_FAILED)
      {
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
        ProcessRequest(header, message);
      }
      finally
      {
        ((IDisposable)message).Dispose();
      }
    }

    /// <summary>Create a request message and validate its native-message interface.</summary>
    private MessageInternals CreateMessage()
    {
      I msg = new I();
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

    /// <summary>
    /// Populates managed fields with native values and calls the callback with the created message
    /// </summary>
    /// <remarks>Sending the Response is also takes care of by this method</remarks>
    /// <param name="message">Message that will be populated and provided to the callback</param>
    /// <param name="header">request id received when taking the Request</param>
    private void ProcessRequest(rcl_rmw_request_id_t header, MessageInternals message)
    {
      message.ReadNativeMessage();
      O response = callback((I)message);
      try
      {
        SendResp(header, response);
      }
      finally
      {
        if (!object.ReferenceEquals(response, message))
        {
          response?.Dispose();
        }
      }
    }

    ~Service()
    {
      Dispose(false);
    }

    /// <summary>Release the service and its native options.</summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Release this service during node disposal without re-entering node removal.</summary>
    void INodeChildEntity.DisposeFromNode(bool disposing)
    {
      Dispose(disposing);
      if (disposing)
      {
        GC.SuppressFinalize(this);
      }
    }

    /// <summary>Shared service disposal path used by explicit disposal, node disposal, and finalization.</summary>
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
            int ret = NativeRcl.rcl_service_fini(ref serviceHandle, ref node.nodeHandle);
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
            if (serviceOptions != IntPtr.Zero)
            {
              NativeRclInterface.rclcs_service_dispose_options(serviceOptions);
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
            serviceOptions = IntPtr.Zero;
            disposed = true;
          }
        }
      }

      if (disposing)
      {
        Ros2csLogger.GetInstance().LogInfo("Service destroyed");
        Utils.ThrowCollectedExceptions(disposeExceptions);
      }
    }
  }
}
