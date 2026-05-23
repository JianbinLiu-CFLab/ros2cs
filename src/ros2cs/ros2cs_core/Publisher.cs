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
// - Added node-owned disposal path.
// - Added QoS/options cleanup and owning-node shutdown guards.

using System;
using ROS2.Internal;

namespace ROS2
{
  /// <summary> Publisher of a topic with a given type and node-owned native lifetime. </summary>
  /// <description> Publishers are created through INode.CreatePublisher </description>
  public class Publisher<T>: IPublisher<T>, INodeChildEntity where T : Message, new ()
  {
    public string Topic { get { return topic; } }
    private string topic;

    private Ros2csLogger logger = Ros2csLogger.GetInstance();
    // Native publisher/options handles are finalized before the owning node is finalized.
    private rcl_publisher_t publisherHandle;
    private IntPtr publisherOptions = IntPtr.Zero;
    // Keep the owning node reference so fini calls always use the current native node handle.
    private readonly Node node;
    private readonly object mutex = new object();
    private bool disposed = false;

    public bool IsDisposed { get { return disposed; } }

    /// <summary> Internal constructor for Publsher. Use INode.CreatePublisher to construct </summary>
    /// <see cref="INode.CreatePublisher"/>
    public Publisher(string pubTopic, Node node, QualityOfServiceProfile qos = null)
    {
      topic = pubTopic;
      this.node = node;

      QualityOfServiceProfile qualityOfServiceProfile = qos;
      bool ownsQos = false;
      if (qualityOfServiceProfile == null)
      {
        qualityOfServiceProfile = new QualityOfServiceProfile();
        ownsQos = true;
      }

      try
      {
        publisherOptions = NativeRclInterface.rclcs_publisher_create_options(qualityOfServiceProfile.Handle);
        if (publisherOptions == IntPtr.Zero)
        {
          throw new RuntimeError("Failed to create publisher options");
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

      publisherHandle = NativeRcl.rcl_get_zero_initialized_publisher();
      try
      {
        Utils.CheckReturnEnum(NativeRcl.rcl_publisher_init(
                                ref publisherHandle,
                                ref node.nodeHandle,
                                typeSupportHandle,
                                topic,
                                publisherOptions));
      }
      catch
      {
        NativeRclInterface.rclcs_publisher_dispose_options(publisherOptions);
        publisherOptions = IntPtr.Zero;
        throw;
      }
    }

    ~Publisher()
    {
      Dispose(false);
    }

    /// <summary>Release the publisher and its native options.</summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Release this publisher during node disposal without re-entering node removal.</summary>
    void INodeChildEntity.DisposeFromNode(bool disposing)
    {
      Dispose(disposing);
      if (disposing)
      {
        GC.SuppressFinalize(this);
      }
    }

    /// <summary>Shared publisher disposal path used by explicit disposal, node disposal, and finalization.</summary>
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
          if (!node.IsDisposed)
          {
            int ret = NativeRcl.rcl_publisher_fini(ref publisherHandle, ref node.nodeHandle);
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
            disposeException = e;
          }
        }
        finally
        {
          try
          {
            if (publisherOptions != IntPtr.Zero)
            {
              NativeRclInterface.rclcs_publisher_dispose_options(publisherOptions);
            }
          }
          catch (Exception e)
          {
            if (disposing && disposeException == null)
            {
              disposeException = e;
            }
          }
          finally
          {
            publisherOptions = IntPtr.Zero;
            disposed = true;
          }
        }
      }

      if (disposing)
      {
        logger.LogInfo("Publisher destroyed");
        if (disposeException != null)
        {
          throw disposeException;
        }
      }
    }

    /// <summary> Publish a message </summary>
    /// <see cref="IPublisher.Publish"/>
    public void Publish(T msg)
    {
      lock (mutex)
      {
        if (!Ros2cs.Ok() || disposed)
        {
          logger.LogWarning("Cannot publish as the class is already disposed or shutdown was called");
          return;
        }
        if (node.IsDisposed)
        {
          // Publishing after node disposal would pass an invalid rcl node-owned handle.
          logger.LogWarning("Cannot publish as the owning node is already disposed");
          return;
        }

        MessageInternals msgInternals = MessageTypeSupportHelper.AsMessageInternals(msg, nameof(msg));
        msgInternals.WriteNativeMessage();
        Utils.CheckReturnEnum(NativeRcl.rcl_publish(ref publisherHandle, msgInternals.Handle, IntPtr.Zero));
      }
    }
  }
}
