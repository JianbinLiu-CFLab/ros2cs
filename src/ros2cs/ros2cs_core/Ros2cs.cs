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
  /// <remarks> This class interfaces with rcl library to handle initalization, shutdown,
  /// creation and removal of nodes as well as spinning (no executors are implemented).
  /// Note that the interface is through rcl and not rclcpp, the primary reason is that marshalling
  /// into generic interface api is not feasible, especially when we don't know all possible instantiations
  /// (as it is the case with custom generated messages).
  /// </remarks>
  public static class Ros2cs
  {
    private static readonly Destructor destructor = new Destructor();
    private static readonly object mutex = new object();
    // Guards native context validation/finalization without participating in node lock ordering.
    private static readonly object contextMutex = new object();
    // Wait set access is serialized separately so shutdown waits for at most the active SpinOnce timeout.
    private static readonly object waitSetMutex = new object();
    private const int FinalizerWaitSetMutexTimeoutMs = 100;
    [ThreadStatic]
    // Tracks callback execution per spinning thread to reject deadlock-prone synchronous client calls.
    private static int spinCallbackDepth;
    [ThreadStatic]
    private static int spinInvocationDepth;
    [ThreadStatic]
    private static ISubscriptionBase[] spinSubscriptionSnapshot;
    [ThreadStatic]
    private static IClientBase[] spinClientSnapshot;
    [ThreadStatic]
    private static IServiceBase[] spinServiceSnapshot;
    private static volatile bool initialized = false;  // for most part equivalent to rcl::ok()
    // true before first Init() so FiniContext() is a no-op in the pre-init state.
    // After Init(), it prevents rcl_context_fini from running twice across shutdown/finalization.
    private static volatile bool contextFinalized = true;
    private static rcl_context_t global_context;  // a simplification, we only use global default context
    private static rcl_allocator_t default_allocator;
    private static List<INode> nodes = new List<INode>(); // kept to shutdown everything in order
    private static readonly Lazy<string> RmwImplementation =
      new Lazy<string>(() => Utils.PtrToString(NativeRmwInterface.rmw_native_interface_get_implementation_identifier()));
    private const string DirectSpinFallbackEnvVar = "ROS2CS_SPIN_FALLBACK";
    private static WaitSet WaitSet;

    /// <summary>Expose the process wait set to friend tests without widening the public API.</summary>
    internal static WaitSet GetWaitSetForTests()
    {
      return WaitSet;
    }

    /// <summary>Expose the wait-set gate to deterministic lifecycle tests.</summary>
    internal static object GetWaitSetMutexForTests()
    {
      return waitSetMutex;
    }

    /// <summary>Report whether a wait-set native wait is currently in progress.</summary>
    internal static bool IsWaitSetWaitActiveForTests
    {
      get { return waitSetWaitActive; }
    }

    internal static bool IsSpinActiveForTests
    {
      get { return waitSetWaitActive || directSpinWaitActive; }
    }

    internal static string ActiveSpinModeForTests
    {
      get { return useDirectSpinFallback ? "direct" : "wait-set"; }
    }

    /// <summary>Enter the wait-set gate for explicit disposal or bounded finalization.</summary>
    internal static bool TryEnterWaitSetMutex(bool explicitDisposal)
    {
      if (explicitDisposal)
      {
        Monitor.Enter(waitSetMutex);
        return true;
      }

      return Monitor.TryEnter(waitSetMutex, FinalizerWaitSetMutexTimeoutMs);
    }

    /// <summary>Leave the wait-set gate acquired by a disposal path.</summary>
    internal static void ExitWaitSetMutex()
    {
      Monitor.Exit(waitSetMutex);
    }
    private static bool destructorFinalizerSuppressed;
    private static volatile bool useDirectSpinFallback;
    private static volatile string directSpinFallbackReason;
    private static int directSpinFallbackLogged;
    private static volatile bool waitSetWaitActive;
    private static volatile bool directSpinWaitActive;

    /// <summary>Whether the current thread is executing subscription/client/service callbacks.</summary>
    internal static bool IsInSpinCallback
    {
      get { return spinCallbackDepth > 0; }
    }

    /// <summary> Globally initialize ros2 (rcl) </summary>
    /// <remarks> Note that only a single context is used. If needed, support for multiple
    /// contexts can be added in a rather straightforward way throughout api. </remarks>
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
          useDirectSpinFallback = ResolveDirectSpinFallback(out directSpinFallbackReason);
          directSpinFallbackLogged = 0;
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

    /// <summary>Return the active RMW implementation identifier reported by the native rmw layer.</summary>
    public static string GetRMWImplementation()
    {
      return RmwImplementation.Value;
    }

    /// <summary> Globally shutdown ros2 (rcl) </summary>
    /// <remarks> Can be called multiple times with no effects after the first one.
    /// Shutdowns ros2 and disposes all the nodes. Ok() function will return false after Shutdown is called.
    /// </remarks>
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

        // Wait for any in-flight rcl_wait before finalizing entity handles stored in the wait set.
        lock (waitSetMutex)
        {
          // Dispose nodes before rcl_shutdown so child handles can finalize against a valid context.
          foreach (var node in nodes)
          {
            try
            {
              ((Node)node).DisposeFromShutdown();
            }
            catch (Exception e)
            {
              AddException(ref exceptions, e);
            }
          }
          nodes.Clear();

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

        useDirectSpinFallback = false;
        directSpinFallbackReason = null;
        directSpinFallbackLogged = 0;

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
    /// <remarks>
    /// Only when this function returns true a node can be created and spinning works
    /// </remarks>
    public static bool Ok()
    {
      return initialized && !contextFinalized;
    }

    /// <summary> Helper class to handle Ros2cs finalization </summary>
    /// <remarks> Could be understood as Ros2cs destructor. Can be called from GC if Shutdown
    /// was not called explicitly. Also, handles context finalization. </remarks>
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
    /// <remarks> Creates a node in the global context and adds it to an internal collection.
    /// Checks for name uniqueness. Throws if name is not unique or Ok() is not true. </remarks>
    /// <param name="nodeName"> A valid node name, which will be first checked for uniqueness,
    /// then validated inside rcl according to naming rules (will throw exception if invalid). </param>
    /// <returns> INode interface, which can be used to create subs and pubs </returns>
    public static INode CreateNode(string nodeName)
    {
      return CreateNodeCore(nodeName, NodeOptions.Default);
    }

    /// <summary> Create a ros2 (rcl) node using explicit node options. </summary>
    /// <remarks> Creates a node in the global context and adds it to an internal collection.
    /// Checks for name uniqueness. Throws if name is not unique or Ok() is not true. </remarks>
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
    /// <remarks> Spin should be called in a dedicate spinning thread in your
    /// application layer since it runs in a blocking infinite loop. Will return when some work is
    /// executed (a callback for each subscription that received a message) or after a timeout.
    /// Note that you don't need to spin if you are only publishing (like in ros2).
    /// Only subscriptions are executed currently, no timers or other executables.
    /// Shutdown waits for an in-flight wait-set wait to return, so shutdown latency can be up to
    /// timeoutSec. If no entities are registered, SpinOnce returns immediately and Shutdown does not
    /// wait for the caller-side empty-spin sleep. </remarks>
    /// <param name="node"> A node to spin on </param>
    /// <param name="timoutSec"> Maximum time to wait for execution item (e. g. subscription) </param>
    public static void Spin(INode node, double timeoutSec = 0.1)
    {
      ValidateSpinTimeout(timeoutSec);
      while (initialized)
      {
        if (!SpinOnce(node, timeoutSec))
        {
          // No entities registered; sleep to avoid busy-waiting before retrying.
          Thread.Sleep(TimeSpan.FromSeconds(timeoutSec));
        }
      }
    }

    /// <summary> Spin overload for multiple nodes </summary>
    /// <remarks> This overload saves on implicit List creation. Shutdown waits for an in-flight
    /// wait-set wait to return, so shutdown latency can be up to timeoutSec. Empty-spin sleeps are
    /// outside the shutdown serialization path. </remarks>
    /// <see cref="Spin(INode,double)"/>
    public static void Spin(List<INode> nodes, double timeoutSec = 0.1)
    {
      ValidateSpinTimeout(timeoutSec);
      while (initialized)
      {
        if (!SpinOnce(nodes, timeoutSec))
        {
          // No entities registered; sleep to avoid busy-waiting before retrying.
          Thread.Sleep(TimeSpan.FromSeconds(timeoutSec));
        }
      }
    }

    /// <summary> Spin only once </summary>
    /// <remarks> This overload is meant for when the while loop is better to
    /// handle in the application layer. </remarks>
    /// <returns> Whether the wait set was populated and waited on. Timeout with entities returns true. </returns>
    /// <see cref="Spin(INode,double)"/>
    public static bool SpinOnce(INode node, double timeoutSec = 0.1)
    {
      ValidateSpinTimeout(timeoutSec);
      return SpinOnceCore(node, null, timeoutSec);
    }

    /// <summary> SpinOnce overload for multiple nodes </summary>
    /// <remarks> This overload saves on implicit List creation. Shutdown waits for an in-flight
    /// spin wait to return, so shutdown latency can be up to timeoutSec. </remarks>
    /// <returns> Whether the wait set was populated and waited on. Timeout with entities returns true. </returns>
    /// <see cref="SpinOnce(INode,double)"/>
    public static bool SpinOnce(List<INode> nodes, double timeoutSec = 0.1)
    {
      ValidateSpinTimeout(timeoutSec);
      return SpinOnceCore(null, nodes, timeoutSec);
    }

    private static void ValidateSpinTimeout(double timeoutSec)
    {
      if (timeoutSec < 0)
      {
        throw new ArgumentOutOfRangeException(nameof(timeoutSec));
      }
    }

    /// <summary>Shared spin path for single-node and multi-node public overloads.</summary>
    private static bool SpinOnceCore(INode singleNode, List<INode> nodes, double timeoutSec)
    {
      bool useThreadStaticSnapshot = spinInvocationDepth == 0;
      spinInvocationDepth++;
      ISubscriptionBase[] allSubscriptions = useThreadStaticSnapshot
        ? spinSubscriptionSnapshot
        : null;
      IClientBase[] allClients = useThreadStaticSnapshot ? spinClientSnapshot : null;
      IServiceBase[] allServices = useThreadStaticSnapshot ? spinServiceSnapshot : null;
      int subscriptionCount = 0;
      int clientCount = 0;
      int serviceCount = 0;
      string directSpinFallbackLogReason = null;

      try
      {
        lock (mutex)
        {
          if (!initialized)
          {
            return false;
          }

          // Snapshot entity collections under the global/node locks, then release them before waiting.
          if (singleNode != null)
          {
            AppendNodeEntities(
              singleNode,
              ref allSubscriptions,
              ref subscriptionCount,
              ref allClients,
              ref clientCount,
              ref allServices,
              ref serviceCount);
          }
          else
          {
            foreach (INode nodeInterface in nodes)
            {
              AppendNodeEntities(
                nodeInterface,
                ref allSubscriptions,
                ref subscriptionCount,
                ref allClients,
                ref clientCount,
                ref allServices,
                ref serviceCount);
            }
          }
        }

        if (subscriptionCount == 0 && clientCount == 0 && serviceCount == 0)
        {
          return false;
        }

        bool success;
        lock (waitSetMutex)
        {
          if (!initialized)
          {
            return false;
          }

          if (useDirectSpinFallback)
          {
            if (Interlocked.Exchange(ref directSpinFallbackLogged, 1) == 0)
            {
              directSpinFallbackLogReason = directSpinFallbackReason;
            }
            try
            {
              directSpinWaitActive = true;
              SleepForSpinTimeout(timeoutSec);
              success = true;
            }
            finally
            {
              directSpinWaitActive = false;
            }
          }
          else
          {
            if (WaitSet == null)
            {
              return false;
            }

            WaitSet.Resize(
              (ulong)subscriptionCount,
              (ulong)clientCount,
              (ulong)serviceCount
            );
            for (int i = 0; i < subscriptionCount; i++)
            {
              AddResult result = WaitSet.TryAddSubscription(allSubscriptions[i], out ulong _);
              ThrowIfWaitSetFull(result, "subscription");
            }
            for (int i = 0; i < clientCount; i++)
            {
              AddResult result = WaitSet.TryAddClient(allClients[i], out ulong _);
              ThrowIfWaitSetFull(result, "client");
            }
            for (int i = 0; i < serviceCount; i++)
            {
              AddResult result = WaitSet.TryAddService(allServices[i], out ulong _);
              ThrowIfWaitSetFull(result, "service");
            }
            try
            {
              waitSetWaitActive = true;
              success = WaitSet.Wait(TimeSpan.FromSeconds(timeoutSec));
            }
            catch (WaitSetEmptyException)
            {
              return false;
            }
            finally
            {
              waitSetWaitActive = false;
            }
          }
        }

        if (directSpinFallbackLogReason != null)
        {
          LogDirectSpinFallback(directSpinFallbackLogReason);
        }

        if (success)
        {
          if (!Ros2cs.Ok())
          {
            return false;
          }

          // Mark callback execution so synchronous client calls can fail fast instead of blocking spin.
          spinCallbackDepth++;
          try
          {
            // Sequential processing. Isolate each entity so one user callback cannot stop the whole batch.
            for (int i = 0; i < subscriptionCount; i++)
            {
              TryTakeMessage(allSubscriptions[i]);
            }
            for (int i = 0; i < clientCount; i++)
            {
              TryTakeMessage(allClients[i]);
            }
            for (int i = 0; i < serviceCount; i++)
            {
              TryTakeMessage(allServices[i]);
            }
          }
          finally
          {
            spinCallbackDepth--;
          }
        }
        return true;
      }
      finally
      {
        ClearSnapshot(allSubscriptions, subscriptionCount);
        ClearSnapshot(allClients, clientCount);
        ClearSnapshot(allServices, serviceCount);
        if (useThreadStaticSnapshot)
        {
          spinSubscriptionSnapshot = allSubscriptions;
          spinClientSnapshot = allClients;
          spinServiceSnapshot = allServices;
        }
        spinInvocationDepth--;
      }
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

    /// <summary>Use a bounded direct take loop when explicitly requested or for known preview distro instability.</summary>
    private static bool ResolveDirectSpinFallback(out string reason)
    {
      string configuredFallback = Environment.GetEnvironmentVariable(DirectSpinFallbackEnvVar);
      if (IsTruthy(configuredFallback))
      {
        reason = DirectSpinFallbackEnvVar + "=" + configuredFallback;
        return true;
      }
      if (IsFalsy(configuredFallback))
      {
        reason = null;
        return false;
      }

      bool lyrical = String.Equals(
        Environment.GetEnvironmentVariable("ROS_DISTRO"),
        "lyrical",
        StringComparison.OrdinalIgnoreCase);
      reason = lyrical ? "ROS_DISTRO=lyrical" : null;
      return lyrical;
    }

    /// <summary>Emit the direct spin fallback mode once as informational runtime context.</summary>
    private static void LogDirectSpinFallback(string reason)
    {
      Ros2csLogger.GetInstance().LogInfo(
        reason + " detected; using direct spin fallback without rcl_wait. " +
        "Shutdown remains serialized, but callback delivery is bounded by the spin timeout.");
    }

    private static bool IsTruthy(string value)
    {
      return String.Equals(value, "direct", StringComparison.OrdinalIgnoreCase) ||
        String.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        String.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFalsy(string value)
    {
      return String.Equals(value, "waitset", StringComparison.OrdinalIgnoreCase) ||
        String.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
        String.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Append entities from a concrete ros2cs node, ignoring foreign/disposed interface instances.</summary>
    private static void AppendNodeEntities(
      INode nodeInterface,
      ref ISubscriptionBase[] allSubscriptions,
      ref int subscriptionCount,
      ref IClientBase[] allClients,
      ref int clientCount,
      ref IServiceBase[] allServices,
      ref int serviceCount)
    {
      Node node = nodeInterface as Node;
      if (node == null)
      {
        return;
      }

      node.AppendEntities(
        ref allSubscriptions,
        ref subscriptionCount,
        ref allClients,
        ref clientCount,
        ref allServices,
        ref serviceCount);
    }

    /// <summary>Clear references held by a completed spin snapshot.</summary>
    private static void ClearSnapshot<T>(T[] snapshot, int count)
      where T : class
    {
      if (snapshot != null && count > 0)
      {
        Array.Clear(snapshot, 0, count);
      }
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
