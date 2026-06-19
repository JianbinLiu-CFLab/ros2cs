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
// - Added child entity disposal contract for node-owned resources.
// - Disposes child entities before node shutdown and aggregates errors.
// - Removes entities even when disposal throws.
// - Added default node option allocation failure handling.
// - Removed redundant DestroyNode alias in favor of Dispose.
// - Made node disposal state volatile for child entity shutdown visibility.
// - Propagates native node option finalization failures during explicit disposal.
// - Added opt-in node options while keeping default creation behavior unchanged.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ROS2
{
  /// <summary>Internal contract for entities whose native handles are owned by a node.</summary>
  internal interface INodeChildEntity : IDisposable
  {
    /// <summary>Dispose from the owning node without recursively removing the entity from that node.</summary>
    void DisposeFromNode(bool disposing);
  }

  /// <summary> Represents a managed ros2 (rcl) node </summary>
  /// <see cref="INode"/>
  public class Node: INode
  {
    public string Name { get { return name; } }
    private string name;
    private Ros2csLogger logger = Ros2csLogger.GetInstance();

    internal rcl_node_t nodeHandle;
    private IntPtr defaultNodeOptions;
    private HashSet<ISubscriptionBase> subscriptions;
    private HashSet<IPublisherBase> publishers;
    private HashSet<IClientBase> clients;
    private HashSet<IServiceBase> services;
    private readonly object mutex = new object();
    private static readonly TimeSpan GraphPollInterval = TimeSpan.FromMilliseconds(100);
    // Child entities read this outside node.mutex while holding their own mutex. Volatile preserves
    // visibility after rcl_node_fini without reversing the node -> child disposal lock order.
    private volatile bool disposed = false;

    public bool IsDisposed { get { return disposed; } }

    /// <summary> Node constructor </summary>
    /// <description> Nodes are created through CreateNode method of Ros2cs class </description>
    /// <param name="nodeName"> unique, non-namespaced node name </param>
    /// <param name="context"> (rcl) context for the node. Global context is passed to this method </param>
    internal Node(string nodeName, ref rcl_context_t context)
      : this(nodeName, ref context, NodeOptions.Default)
    {
    }

    /// <summary> Node constructor </summary>
    /// <description> Nodes are created through CreateNode method of Ros2cs class </description>
    /// <param name="nodeName"> unique, non-namespaced node name </param>
    /// <param name="context"> (rcl) context for the node. Global context is passed to this method </param>
    /// <param name="options"> managed node options applied to native defaults before rcl_node_init </param>
    internal Node(string nodeName, ref rcl_context_t context, NodeOptions options)
    {
      if (options == null)
      {
        throw new ArgumentNullException(nameof(options));
      }
      name = nodeName;
      string nodeNamespace = "/";
      subscriptions = new HashSet<ISubscriptionBase>();
      publishers = new HashSet<IPublisherBase>();
      clients = new HashSet<IClientBase>();
      services = new HashSet<IServiceBase>();

      nodeHandle = NativeRcl.rcl_get_zero_initialized_node();
      defaultNodeOptions = NativeRclInterface.rclcs_node_create_default_options();
      if (defaultNodeOptions == IntPtr.Zero)
      {
        throw new RuntimeError("Failed to create node options");
      }
      try
      {
        Utils.CheckReturnEnum(NativeRclInterface.rclcs_node_options_set_enable_rosout(
          defaultNodeOptions,
          options.EnableRosout));
        Utils.CheckReturnEnum(NativeRcl.rcl_node_init(ref nodeHandle, nodeName, nodeNamespace, ref context, defaultNodeOptions));
      }
      catch (Exception initException)
      {
        List<Exception> exceptions = new List<Exception> { initException };
        try
        {
          Utils.CheckReturnEnum(NativeRclInterface.rclcs_node_dispose_options(defaultNodeOptions));
        }
        catch (Exception disposeException)
        {
          exceptions.Add(disposeException);
        }
        finally
        {
          defaultNodeOptions = IntPtr.Zero;
        }
        Utils.ThrowCollectedExceptions(exceptions);
      }
      logger.LogInfo("Node initialized");
    }

    /// <summary> Finalizer supporting IDisposable model </summary>
    ~Node()
    {
      Dispose(false);
    }

    /// <summary> Release managed and native resources. IDisposable implementation </summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Shared node disposal path used by explicit disposal and the finalizer.</summary>
    private void Dispose(bool disposing)
    {
      lock (mutex)
      {
        if (disposed)
        {
          return;
        }

        List<Exception> exceptions = null;

        // Even finalizer cleanup must fini children before rcl_node_fini; rcl does not cascade this.
        DisposeChildren(subscriptions, disposing, ref exceptions);
        subscriptions.Clear();

        DisposeChildren(publishers, disposing, ref exceptions);
        publishers.Clear();

        DisposeChildren(clients, disposing, ref exceptions);
        clients.Clear();

        DisposeChildren(services, disposing, ref exceptions);
        services.Clear();

        try
        {
          int ret = NativeRcl.rcl_node_fini(ref nodeHandle);
          if (disposing)
          {
            Utils.CheckReturnEnum(ret);
          }
        }
        catch (Exception e)
        {
          if (disposing)
          {
            AddException(ref exceptions, e);
          }
        }
        finally
        {
          try
          {
            if (defaultNodeOptions != IntPtr.Zero)
            {
              Utils.CheckReturnEnum(NativeRclInterface.rclcs_node_dispose_options(defaultNodeOptions));
            }
          }
          catch (Exception e)
          {
            if (disposing)
            {
              AddException(ref exceptions, e);
            }
          }
          finally
          {
            defaultNodeOptions = IntPtr.Zero;
            disposed = true;
          }
        }

        if (disposing)
        {
          logger.LogInfo("Node " + name + " destroyed");
          if (exceptions != null && exceptions.Count > 0)
          {
            throw new AggregateException(exceptions);
          }
        }
      }
    }

    /// <summary>Copy live waitable entities into spin-local lists while holding the node lock.</summary>
    internal void AppendEntities(
      List<ISubscriptionBase> targetSubscriptions,
      List<IClientBase> targetClients,
      List<IServiceBase> targetServices)
    {
      lock (mutex)
      {
        AppendLiveEntities(subscriptions, targetSubscriptions);
        AppendLiveEntities(clients, targetClients);
        AppendLiveEntities(services, targetServices);
      }
    }

    /// <summary>Copy only live child entities into a spin-local snapshot.</summary>
    private static void AppendLiveEntities<T>(IEnumerable<T> source, List<T> target)
      where T : class, IExtendedDisposable
    {
      foreach (T entity in source)
      {
        if (!entity.IsDisposed)
        {
          target.Add(entity);
        }
      }
    }

    /// <summary>Dispose child entities and aggregate explicit-dispose failures.</summary>
    private static void DisposeChildren<T>(IEnumerable<T> children, bool disposing, ref List<Exception> exceptions)
      where T : IDisposable
    {
      foreach (T child in children.ToList())
      {
        try
        {
          if (child is INodeChildEntity nodeChild)
          {
            nodeChild.DisposeFromNode(disposing);
          }
          else if (disposing)
          {
            child.Dispose();
          }
        }
        catch (Exception e)
        {
          if (disposing)
          {
            AddException(ref exceptions, e);
          }
        }
      }
    }

    /// <summary>Lazy-create the exception collection used during coordinated shutdown.</summary>
    private static void AddException(ref List<Exception> exceptions, Exception exception)
    {
      if (exceptions == null)
      {
        exceptions = new List<Exception>();
      }
      exceptions.Add(exception);
    }

    /// <summary>Reject node operations when the owning node or global context is no longer live.</summary>
    private void ThrowIfNodeNotUsable(string operationDescription)
    {
      if (disposed)
      {
        throw new ObjectDisposedException(nameof(Node), "Cannot " + operationDescription + " as the node is already disposed");
      }
      if (!Ros2cs.Ok())
      {
        logger.LogWarning("Cannot " + operationDescription + " as shutdown was called");
        throw new NotInitializedException();
      }
    }

    /// <summary> Count publishers currently visible for a topic in this node's local ROS graph cache. </summary>
    /// <see cref="INode.CountPublishers"/>
    public int CountPublishers(string topicName)
    {
      lock (mutex)
      {
        ThrowIfNodeNotUsable("query publisher count");

        UIntPtr count = UIntPtr.Zero;
        Utils.CheckReturnEnum(NativeRcl.rcl_count_publishers(ref nodeHandle, topicName, ref count));
        return checked((int)count.ToUInt64());
      }
    }

    /// <summary> Count subscribers currently visible for a topic in this node's local ROS graph cache. </summary>
    /// <see cref="INode.CountSubscribers"/>
    public int CountSubscribers(string topicName)
    {
      lock (mutex)
      {
        ThrowIfNodeNotUsable("query subscriber count");

        UIntPtr count = UIntPtr.Zero;
        Utils.CheckReturnEnum(NativeRcl.rcl_count_subscribers(ref nodeHandle, topicName, ref count));
        return checked((int)count.ToUInt64());
      }
    }

    /// <summary> Wait up to a bounded timeout for a publisher to become visible for a topic. </summary>
    /// <see cref="INode.TryWaitForPublisher"/>
    public bool TryWaitForPublisher(string topicName, TimeSpan timeout)
    {
      return TryWaitForGraphCondition(() => CountPublishers(topicName) > 0, timeout);
    }

    /// <summary> Wait up to a bounded timeout for a subscriber to become visible for a topic. </summary>
    /// <see cref="INode.TryWaitForSubscriber"/>
    public bool TryWaitForSubscriber(string topicName, TimeSpan timeout)
    {
      return TryWaitForGraphCondition(() => CountSubscribers(topicName) > 0, timeout);
    }

    /// <summary>Poll a ROS graph condition until it succeeds or the timeout elapses.</summary>
    private static bool TryWaitForGraphCondition(Func<bool> condition, TimeSpan timeout)
    {
      if (timeout < TimeSpan.Zero)
      {
        throw new ArgumentOutOfRangeException(nameof(timeout));
      }

      DateTime deadline = DateTime.UtcNow + timeout;
      while (true)
      {
        if (condition())
        {
          return true;
        }

        TimeSpan remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
          return false;
        }

        Thread.Sleep(remaining < GraphPollInterval ? remaining : GraphPollInterval);
      }
    }

    /// <summary> Get topic names and type names currently visible in this node's local ROS graph cache. </summary>
    /// <see cref="INode.GetTopicNamesAndTypes"/>
    public IReadOnlyList<TopicNamesAndTypes> GetTopicNamesAndTypes(bool noDemangle = false)
    {
      IntPtr result = IntPtr.Zero;
      lock (mutex)
      {
        try
        {
          ThrowIfNodeNotUsable("query topic names and types");
          Utils.CheckReturnEnum(NativeRclInterface.rclcs_get_topic_names_and_types(
            ref nodeHandle,
            noDemangle,
            out result));
          return MarshalTopicNamesAndTypes(result);
        }
        finally
        {
          NativeRclInterface.rclcs_dispose_topic_names_and_types(result);
        }
      }
    }

    /// <summary>Copy flattened native graph data into managed immutable snapshots.</summary>
    private static IReadOnlyList<TopicNamesAndTypes> MarshalTopicNamesAndTypes(IntPtr result)
    {
      if (result == IntPtr.Zero)
      {
        return new List<TopicNamesAndTypes>().AsReadOnly();
      }

      rclcs_topic_names_and_types_t nativeResult =
        Marshal.PtrToStructure<rclcs_topic_names_and_types_t>(result);
      int topicCount = checked((int)nativeResult.size.ToUInt64());
      List<TopicNamesAndTypes> topics = new List<TopicNamesAndTypes>(topicCount);
      int typeArraySize = Marshal.SizeOf<rclcs_string_array_t>();

      for (int i = 0; i < topicCount; i++)
      {
        IntPtr namePtr = Marshal.ReadIntPtr(nativeResult.names, i * IntPtr.Size);
        string topicName = Marshal.PtrToStringAnsi(namePtr) ?? String.Empty;

        IntPtr typeArrayPtr = IntPtr.Add(nativeResult.types, i * typeArraySize);
        rclcs_string_array_t nativeTypes =
          Marshal.PtrToStructure<rclcs_string_array_t>(typeArrayPtr);
        int typeCount = checked((int)nativeTypes.size.ToUInt64());
        string[] types = new string[typeCount];
        for (int j = 0; j < typeCount; j++)
        {
          IntPtr typePtr = Marshal.ReadIntPtr(nativeTypes.data, j * IntPtr.Size);
          types[j] = Marshal.PtrToStringAnsi(typePtr) ?? String.Empty;
        }

        topics.Add(new TopicNamesAndTypes(topicName, Array.AsReadOnly(types)));
      }

      return topics.AsReadOnly();
    }

    /// <summary> Create a client for this node for a given topic, qos and message type </summary>
    /// <see cref="INode.CreateClient"/>
    public Client<I, O> CreateClient<I, O>(string topic, QualityOfServiceProfile qos = null) where I : Message, new() where O : Message, new()
    {
      lock (mutex)
      {
        ThrowIfNodeNotUsable("create client");

        Client<I, O> client = new Client<I, O>(topic, this, qos);
        clients.Add(client);
        logger.LogInfo("Created Client for topic " + topic);
        return client;
      }
    }
    /// <summary> Remove a client </summary>
    /// <see cref="INode.RemoveClient"/>
    public bool RemoveClient(IClientBase client)
    {
      lock (mutex)
      {
        if (clients.Contains(client))
        {
          logger.LogInfo("Removing client for topic " + client.Topic);
          try
          {
            // Child disposal is idempotent; its disposed flag prevents double-fini if node disposal races.
            client.Dispose();
          }
          finally
          {
            clients.Remove(client);
          }
          return true;
        }
        return false;
      }
    }

    /// <summary> Create a service for this node for a given topic, callback, qos and message type </summary>
    /// <see cref="INode.CreateService"/>
    public Service<I, O> CreateService<I, O>(string topic, Func<I, O> callback, QualityOfServiceProfile qos = null) where I : Message, new() where O : Message, new()
    {
      lock (mutex)
      {
        ThrowIfNodeNotUsable("create service");

        Service<I, O> service = new Service<I, O>(topic, this, callback, qos);
        services.Add(service);
        logger.LogInfo("Created service for topic " + topic);
        return service;
      }
    }

    /// <summary> Remove a service </summary>
    /// <see cref="INode.RemoveService"/>
    public bool RemoveService(IServiceBase service)
    {
      lock (mutex)
      {
        if (services.Contains(service))
        {
          logger.LogInfo("Removing service for topic " + service.Topic);
          try
          {
            service.Dispose();
          }
          finally
          {
            services.Remove(service);
          }
          return true;
        }
        return false;
      }
    }

    /// <summary> Create a publisher for this node for a given topic, qos and message type </summary>
    /// <see cref="INode.CreatePublisher"/>
    public Publisher<T> CreatePublisher<T>(string topic, QualityOfServiceProfile qos = null) where T : Message, new()
    {
      lock (mutex)
      {
        ThrowIfNodeNotUsable("create publisher");

        Publisher<T> publisher = new Publisher<T>(topic, this, qos);
        publishers.Add(publisher);
        logger.LogInfo("Created Publisher for topic " + topic);
        return publisher;
      }
    }

    /// <summary> Create a subscription for this node for a given topic, callback, qos and message type </summary>
    /// <see cref="INode.CreateSubscription"/>
    public Subscription<T> CreateSubscription<T>(string topic, Action<T> callback, QualityOfServiceProfile qos = null) where T : Message, new()
    {
      lock (mutex)
      {
        ThrowIfNodeNotUsable("create subscription");

        Subscription<T> subscription = new Subscription<T>(topic, this, callback, qos);
        subscriptions.Add(subscription);
        logger.LogInfo("Created subscription for topic " + topic);
        return subscription;
      }
    }

    /// <summary> Remove a publisher </summary>
    /// <see cref="INode.RemovePublisher"/>
    public bool RemovePublisher(IPublisherBase publisher)
    {
      lock (mutex)
      {
        if (publishers.Contains(publisher))
        {
          logger.LogInfo("Removing publisher for topic " + publisher.Topic);
          try
          {
            publisher.Dispose();
          }
          finally
          {
            publishers.Remove(publisher);
          }
          return true;
        }
        return false;
      }
    }

    /// <summary> Remove a subscription </summary>
    /// <see cref="INode.RemoveSubscription"/>
    public bool RemoveSubscription(ISubscriptionBase subscription)
    {
      lock (mutex)
      {
        if (subscriptions.Contains(subscription))
        {
          logger.LogInfo("Removing subscription for topic " + subscription.Topic);
          try
          {
            subscription.Dispose();
          }
          finally
          {
            subscriptions.Remove(subscription);
          }
          return true;
        }
        return false;
      }
    }
  }
}
