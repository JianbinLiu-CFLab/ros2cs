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
// - Added coordinated shutdown of nodes, wait set, and rcl context.
// - Added spin callback tracking to prevent synchronous call reentry.
// - Serialized wait set access and reduced reconnect/spin noise.
// - Suppressed the static shutdown finalizer after explicit Shutdown.
// - Clarified context and wait-set lifecycle invariants.
// - Pruned directly disposed nodes before enforcing name uniqueness.
// - Added opt-in node options without changing the default CreateNode overload.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace ROS2
{
  /// <summary> Primary ros2 C# static class </summary>
  /// <description> This class interfaces with rcl library to handle initalization, shutdown,
  /// creation and removal of nodes as well as spinning (no executors are implemented).
  /// Note that the interface is through rcl and not rclcpp, the primary reason is that marshalling
  /// into generic interface api is not feasible, especially when we don't know all possible instantiations
  /// (as it is the case with custom generated messages).
  /// </description>
  public static class Ros2cs
  {
    private static readonly Destructor destructor = new Destructor();
    private static readonly object mutex = new object();
    // Guards native context validation/finalization without participating in node lock ordering.
    private static readonly object contextMutex = new object();
    // Wait set access is serialized separately so shutdown waits for at most the active SpinOnce timeout.
    private static readonly object waitSetMutex = new object();
    [ThreadStatic]
    // Tracks callback execution per spinning thread to reject deadlock-prone synchronous client calls.
    private static int spinCallbackDepth;
    private static volatile bool initialized = false;  // for most part equivalent to rcl::ok()
    // true before first Init() so FiniContext() is a no-op in the pre-init state.
    // After Init(), it prevents rcl_context_fini from running twice across shutdown/finalization.
    private static volatile bool contextFinalized = true;
    private static rcl_context_t global_context;  // a simplification, we only use global default context
    private static rcl_allocator_t default_allocator;
    private static List<INode> nodes = new List<INode>(); // kept to shutdown everything in order
    private static readonly Lazy<string> RmwImplementation =
      new Lazy<string>(() => Utils.PtrToString(NativeRmwInterface.rmw_native_interface_get_implementation_identifier()));
    private static readonly Lazy<bool> UseDirectSpinFallback =
      new Lazy<bool>(() => String.Equals(
        Environment.GetEnvironmentVariable("ROS_DISTRO"),
        "lyrical",
        StringComparison.OrdinalIgnoreCase));

    private static WaitSet WaitSet;
    private static bool destructorFinalizerSuppressed;

    /// <summary>Whether the current thread is executing subscription/client/service callbacks.</summary>
    internal static bool IsInSpinCallback
    {
      get { return spinCallbackDepth > 0; }
    }

    /// <summary> Globally initialize ros2 (rcl) </summary>
    /// <description> Note that only a single context is used. </description>
    /// <remarks> If needed, support for multiple contexts can be added
    /// in a rather straightforward way throughout api. </remarks>
    public static void Init()
    {
      lock (mutex)
      {
        if (initialized)
        {
          return;
        }

        lock (contextMutex)
        {
          default_allocator = NativeRcl.rcutils_get_default_allocator();
          global_context = NativeRcl.rcl_get_zero_initialized_context();
          Utils.CheckReturnEnum(NativeRclInterface.rclcs_init(ref global_context, default_allocator));
          WaitSet = new WaitSet(ref global_context, default_allocator);
          initialized = true;
          contextFinalized = false;
        }
        if (destructorFinalizerSuppressed)
        {
          GC.ReRegisterForFinalize(destructor);
          destructorFinalizerSuppressed = false;
        }
      }
    }

    public static string GetRMWImplementation()
    {
      return RmwImplementation.Value;
    }

    /// <summary> Globally shutdown ros2 (rcl) </summary>
    /// <description> Can be called multiple times with no effects after the first one.
    /// Shutdowns ros2 and disposes all the nodes. Ok() function will return false after Shutdown is called.
    /// </description>
    public static void Shutdown()
    {
      List<Exception> exceptions = null;
      lock (mutex)
      {
        if (!initialized)
        {
          return;
        }
        SuppressDestructorFinalizer();
        initialized = false;

        Ros2csLogger.GetInstance().LogInfo("Ros2cs shutdown");

        // Dispose nodes before rcl_shutdown so child handles can finalize against a valid context.
        foreach (var node in nodes)
        {
          try
          {
            node.Dispose();
          }
          catch (Exception e)
          {
            AddException(ref exceptions, e);
          }
        }
        nodes.Clear();

        // The wait set must be released while no SpinOnce call can mutate or wait on it.
        lock (waitSetMutex)
        {
          try
          {
            if (WaitSet != null)
            {
              WaitSet.Dispose();
              WaitSet = null;
            }
          }
          catch (Exception e)
          {
            AddException(ref exceptions, e);
          }
        }

        try
        {
          Utils.CheckReturnEnum(NativeRcl.rcl_shutdown(ref global_context));
        }
        catch (Exception e)
        {
          AddException(ref exceptions, e);
        }

        try
        {
          FiniContext(true);
        }
        catch (Exception e)
        {
          AddException(ref exceptions, e);
        }
      }

      if (exceptions != null && exceptions.Count > 0)
      {
        throw new AggregateException(exceptions);
      }
    }

    /// <summary> Whether ros2 C# is initialized </summary>
    /// <description>
    /// Only when this function returns true a node can be created and spinning works
    /// </description>
    public static bool Ok()
    {
      return initialized && !contextFinalized;
    }

    /// <summary> Helper class to handle Ros2cs finalization </summary>
    /// <description> Could be understood as Ros2cs destructor. Can be called from GC if Shutdown
    /// was not called explicitly. Also, handles context finalization. </description>
    private sealed class Destructor
    {
      ~Destructor()
      {
        try
        {
          Ros2cs.Shutdown();
        }
        catch
        {
        }
      }
    }

    /// <summary>Prevent the static finalizer from re-entering Shutdown after explicit shutdown starts.</summary>
    private static void SuppressDestructorFinalizer()
    {
      if (destructorFinalizerSuppressed)
      {
        return;
      }

      GC.SuppressFinalize(destructor);
      destructorFinalizerSuppressed = true;
    }

    /// <summary> Create a ros2 (rcl) node using default node options. </summary>
    /// <description> Creates a node in the global context and adds it to an internal collection.
    /// Checks for name uniqueness. Throws if name is not unique or Ok() is not true. </description>
    /// <param name="nodeName"> A valid node name, which will be first checked for uniqueness,
    /// then validated inside rcl according to naming rules (will throw exception if invalid). </param>
    /// <returns> INode interface, which can be used to create subs and pubs </returns>
    public static INode CreateNode(string nodeName)
    {
      return CreateNodeCore(nodeName, NodeOptions.Default);
    }

    /// <summary> Create a ros2 (rcl) node using explicit node options. </summary>
    /// <description> Creates a node in the global context and adds it to an internal collection.
    /// Checks for name uniqueness. Throws if name is not unique or Ok() is not true. </description>
    /// <param name="nodeName"> A valid node name, which will be first checked for uniqueness,
    /// then validated inside rcl according to naming rules (will throw exception if invalid). </param>
    /// <param name="options"> Options applied to native node defaults before creation. </param>
    /// <returns> INode interface, which can be used to create subs and pubs </returns>
    public static INode CreateNode(string nodeName, NodeOptions options)
    {
      if (options == null)
      {
        throw new ArgumentNullException(nameof(options));
      }

      return CreateNodeCore(nodeName, options);
    }

    /// <summary>Shared node creation path for default and explicit node options.</summary>
    private static INode CreateNodeCore(string nodeName, NodeOptions options)
    {
      lock (mutex)
      {
        if (!Ok())
        {
          Ros2csLogger.GetInstance().LogError("Ros2cs is not initialized, cannot create node");
          throw new NotInitializedException();
        }

        nodes.RemoveAll(node => node.IsDisposed);
        foreach (var node in nodes)
        {
          if (node.Name == nodeName)
          {
            throw new InvalidOperationException("Node with name " + nodeName + " already exists, cannot create");
          }
        }

        var new_node = new Node(nodeName, ref global_context, options);
        nodes.Add(new_node);
        return new_node;
      }
    }

    /// <summary> Remove and dispose ros2 (rcl) node </summary>
    /// <remarks> You can call Shutdown to dispose all the nodes, this is only needed when
    /// node needs to be removed while others are still meant to be running </remarks>
    /// <param name="node"> a node to remove as returned by previous CreateNode call </param>
    /// <returns> Whether the node was in the internal collection, which should always be true
    /// unless this is called more than once for a node (which is ok). Return value can be
    /// safely ignored <returns>
    public static bool RemoveNode(INode node)
    {
      lock (mutex)
      {
        if (!initialized)
        {
          return false; // removal is handled with shutdown already
        }
        try
        {
          node.Dispose();
        }
        finally
        {
          nodes.Remove(node);
        }
        return true;
      }
    }

    /// <summary> Spin on a single node </summary>
    /// <description> Spin should be called in a dedicate spinning thread in your
    /// application layer since it runs in a blocking infinite loop. Will return when some work is
    /// executed (a callback for each subscription that received a message) or after a timeout.
    /// Note that you don't need to spin if you are only publishing (like in ros2) </description>
    /// <remarks> Only subscriptions are executed currently, no timers or other executables.
    /// Shutdown waits for an in-flight spin wait to return, so shutdown latency can be up to timeoutSec. </remarks>
    /// <param name="node"> A node to spin on </param>
    /// <param name="timoutSec"> Maximum time to wait for execution item (e. g. subscription) </param>
    public static void Spin(INode node, double timeoutSec = 0.1)
    {
      while (initialized)
      {
        if (!SpinOnce(node, timeoutSec))
        {
          Thread.Sleep(TimeSpan.FromSeconds(timeoutSec));
        }
      }
    }

    /// <summary> Spin overload for multiple nodes </summary>
    /// <remarks> This overload saves on implicit List creation. Shutdown waits for an in-flight
    /// spin wait to return, so shutdown latency can be up to timeoutSec. </remarks>
    /// <see cref="Spin(INode,double)"/>
    public static void Spin(List<INode> nodes, double timeoutSec = 0.1)
    {
      while (initialized)
      {
        if (!SpinOnce(nodes, timeoutSec))
        {
          Thread.Sleep(TimeSpan.FromSeconds(timeoutSec));
        }
      }
    }

    /// <summary> Spin only once </summary>
    /// <description> This overload is meant for when the while loop is better to
    /// handle in the application layer  </description>
    /// <returns> Whether the wait set was populated and waited on. Timeout with entities returns true. </returns>
    /// <see cref="Spin(INode,double)"/>
    public static bool SpinOnce(INode node, double timeoutSec = 0.1)
    {
      return SpinOnceCore(node, null, timeoutSec);
    }

    /// <summary> SpinOnce overload for multiple nodes </summary>
    /// <remarks> This overload saves on implicit List creation. Shutdown waits for an in-flight
    /// spin wait to return, so shutdown latency can be up to timeoutSec. </remarks>
    /// <returns> Whether the wait set was populated and waited on. Timeout with entities returns true. </returns>
    /// <see cref="SpinOnce(INode,double)"/>
    public static bool SpinOnce(List<INode> nodes, double timeoutSec = 0.1)
    {
      return SpinOnceCore(null, nodes, timeoutSec);
    }

    /// <summary>Shared spin path for single-node and multi-node public overloads.</summary>
    private static bool SpinOnceCore(INode singleNode, List<INode> nodes, double timeoutSec)
    {
      List<ISubscriptionBase> allSubscriptions;
      List<IClientBase> allClients;
      List<IServiceBase> allServices;
      bool success;

      lock (mutex)
      {
        if (!initialized)
        {
          return false;
        }

        // Snapshot entity collections under the global/node locks, then release them before waiting.
        allSubscriptions = new List<ISubscriptionBase>();
        allClients = new List<IClientBase>();
        allServices = new List<IServiceBase>();
        if (singleNode != null)
        {
          AppendNodeEntities(singleNode, allSubscriptions, allClients, allServices);
        }
        else
        {
          foreach (INode nodeInterface in nodes)
          {
            AppendNodeEntities(nodeInterface, allSubscriptions, allClients, allServices);
          }
        }
      }

      if (allSubscriptions.Count == 0 && allClients.Count == 0 && allServices.Count == 0)
      {
        return false;
      }

      if (UseDirectSpinFallback.Value)
      {
        SleepForSpinTimeout(timeoutSec);
        success = true;
      }
      else
      {
        lock (waitSetMutex)
        {
          if (!initialized || WaitSet == null)
          {
            return false;
          }

          WaitSet.Resize(
            (ulong)allSubscriptions.Count,
            (ulong)allClients.Count,
            (ulong)allServices.Count
          );
          foreach(var subscription in allSubscriptions)
          {
            AddResult result = WaitSet.TryAddSubscription(subscription, out ulong _);
            ThrowIfWaitSetFull(result, "subscription");
          }
          foreach(var client in allClients)
          {
            AddResult result = WaitSet.TryAddClient(client, out ulong _);
            ThrowIfWaitSetFull(result, "client");
          }
          foreach(var service in allServices)
          {
            AddResult result = WaitSet.TryAddService(service, out ulong _);
            ThrowIfWaitSetFull(result, "service");
          }
          try
          {
            success = WaitSet.Wait(TimeSpan.FromSeconds(timeoutSec));
          }
          catch (WaitSetEmptyException)
          {
            return false;
          }
        }
      }

      if (success)
      {
        // Mark callback execution so synchronous client calls can fail fast instead of blocking spin.
        spinCallbackDepth++;
        try
        {
          // Sequential processing. Isolate each entity so one user callback cannot stop the whole batch.
          foreach (ISubscriptionBase subscription in allSubscriptions)
          {
            TryTakeMessage(subscription);
          }
          foreach (IClientBase client in allClients)
          {
            TryTakeMessage(client);
          }
          foreach (IServiceBase service in allServices)
          {
            TryTakeMessage(service);
          }
        }
        finally
        {
          spinCallbackDepth--;
        }
      }
      return true;
    }

    /// <summary>Bounded fallback used by the Lyrical preview where rcl_wait can crash after repeated context cycling.</summary>
    private static void SleepForSpinTimeout(double timeoutSec)
    {
      if (timeoutSec <= 0)
      {
        return;
      }

      Thread.Sleep(TimeSpan.FromSeconds(timeoutSec));
    }

    /// <summary>Append entities from a concrete ros2cs node, ignoring foreign/disposed interface instances.</summary>
    private static void AppendNodeEntities(
      INode nodeInterface,
      List<ISubscriptionBase> allSubscriptions,
      List<IClientBase> allClients,
      List<IServiceBase> allServices)
    {
      Node node = nodeInterface as Node;
      if (node == null)
      {
        return;
      }

      node.AppendEntities(allSubscriptions, allClients, allServices);
    }

    /// <summary>Finalize the global rcl context exactly once.</summary>
    private static void FiniContext(bool throwing)
    {
      lock (contextMutex)
      {
        if (contextFinalized)
        {
          return;
        }

        int ret = NativeRcl.rcl_context_fini(ref global_context);
        contextFinalized = true;
        if (throwing)
        {
          Utils.CheckReturnEnum(ret);
        }
      }
    }

    /// <summary>Run one subscription callback path without letting user exceptions abort the spin batch.</summary>
    private static void TryTakeMessage(ISubscriptionBase subscription)
    {
      TryTakeSubscriptionMessage(subscription);
    }

    /// <summary>Run one client response path without letting user exceptions abort the spin batch.</summary>
    private static void TryTakeMessage(IClientBase client)
    {
      TryTakeClientMessage(client);
    }

    /// <summary>Run one service callback path without letting user exceptions abort the spin batch.</summary>
    private static void TryTakeMessage(IServiceBase service)
    {
      TryTakeServiceMessage(service);
    }

    private static void TryTakeSubscriptionMessage(ISubscriptionBase subscription)
    {
      try
      {
        subscription.TakeMessage();
      }
      catch (Exception e)
      {
        LogTakeMessageException("subscription", subscription.Topic, e);
      }
    }

    private static void TryTakeClientMessage(IClientBase client)
    {
      try
      {
        client.TakeMessage();
      }
      catch (Exception e)
      {
        LogTakeMessageException("client", client.Topic, e);
      }
    }

    private static void TryTakeServiceMessage(IServiceBase service)
    {
      try
      {
        service.TakeMessage();
      }
      catch (Exception e)
      {
        LogTakeMessageException("service", service.Topic, e);
      }
    }

    private static void LogTakeMessageException(string entityKind, string topic, Exception e)
    {
      Ros2csLogger.GetInstance().LogError(
        "Unhandled exception while processing " + entityKind + " '" + topic + "': " + e);
    }

    /// <summary>Fail explicitly when a resized wait set cannot accept all collected entities.</summary>
    private static void ThrowIfWaitSetFull(AddResult result, string entityType)
    {
      if (result == AddResult.FULL)
      {
        throw new InvalidOperationException("No space for " + entityType + " in WaitSet");
      }
    }

    /// <summary>Lazy-create the exception collection used by coordinated shutdown.</summary>
    private static void AddException(ref List<Exception> exceptions, Exception exception)
    {
      if (exceptions == null)
      {
        exceptions = new List<Exception>();
      }
      exceptions.Add(exception);
    }
  }
}
