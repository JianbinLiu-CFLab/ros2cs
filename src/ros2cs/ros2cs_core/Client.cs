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
// - Added node-owned disposal path and graceful request cancellation.
// - Reworked client handle and options ownership.
// - Added response cleanup and spin callback reentry guard.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ROS2.Internal;

namespace ROS2
{
  /// <summary>Client with a topic, message types, and node-owned native lifetime.</summary>
  /// <remarks>Instances are created by <see cref="INode.CreateClient"/></remarks>
  /// <typeparam name="I">Message Type to be send</typeparam>
  /// <typeparam name="O">Message Type to be received</typeparam>
  public class Client<I, O>: IClient<I, O>, INodeChildEntity
    where I : Message, new()
    where O : Message, new()
  {
    /// <inheritdoc/>
    public string Topic { get { return topic; } }

    /// <inheritdoc/>
    public rcl_client_t Handle { get { return clientHandle; } }

    /// <inheritdoc/>
    public IReadOnlyDictionary<long, Task<O>> PendingRequests {get; private set;}

    /// <inheritdoc/>
    IReadOnlyDictionary<long, Task> IClientBase.PendingRequests {get { return (IReadOnlyDictionary<long, Task>)this.PendingRequests; }}

    private string topic;

    /// <inheritdoc/>
    public object Mutex { get { return mutex; } }

    private object mutex = new object();

    /// <summary>
    /// Mapping from request id without Response to <see cref="TaskCompletionSource"/>.
    /// </summary>
    /// <remarks>
    /// The <see cref="TaskCompletionSource.Task"/> is stored separately to allow
    /// <see cref="Cancel"/> to work even if the source returns multiple tasks.
    /// </remarks>
    private Dictionary<long, (TaskCompletionSource<O>, Task<O>)> Requests;

    private Ros2csLogger logger = Ros2csLogger.GetInstance();

    // Native client/options handles are finalized before the owning node is finalized.
    private rcl_client_t clientHandle;

    private IntPtr clientOptions = IntPtr.Zero;

    // Keep the owning node reference so fini calls always use the current native node handle.
    private readonly Node node;

    /// <inheritdoc/>
    public bool IsDisposed { get { return disposed; } }
    private bool disposed = false;

    /// <summary>
    /// Internal constructor for Client
    /// </summary>
    /// <remarks>Use <see cref="INode.CreateClient"/> to construct new Instances</remarks>
    public Client(string pubTopic, Node node, QualityOfServiceProfile qos = null)
    {
      topic = pubTopic;
      this.node = node;

      QualityOfServiceProfile qualityOfServiceProfile = qos;
      bool ownsQos = false;
      if (qualityOfServiceProfile == null)
      {
        qualityOfServiceProfile = new QualityOfServiceProfile(QosPresetProfile.SERVICES_DEFAULT);
        ownsQos = true;
      }

      Requests = new Dictionary<long, (TaskCompletionSource<O>, Task<O>)>();
      PendingRequests = new PendingTasksView(Requests);

      try
      {
        clientOptions = NativeRclInterface.rclcs_client_create_options(qualityOfServiceProfile.Handle);
        if (clientOptions == IntPtr.Zero)
        {
          throw new RuntimeError("Failed to create client options");
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

      clientHandle = NativeRcl.rcl_get_zero_initialized_client();
      try
      {
        Utils.CheckReturnEnum(NativeRcl.rcl_client_init(
                                ref clientHandle,
                                ref node.nodeHandle,
                                typeSupportHandle,
                                topic,
                                clientOptions));
      }
      catch
      {
        NativeRclInterface.rclcs_client_dispose_options(clientOptions);
        clientOptions = IntPtr.Zero;
        throw;
      }
    }

    ~Client()
    {
      Dispose(false);
    }

    /// <summary>Release the client and fail any still-pending requests.</summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Release this client during node disposal without re-entering node removal.</summary>
    void INodeChildEntity.DisposeFromNode(bool disposing)
    {
      Dispose(disposing);
      if (disposing)
      {
        GC.SuppressFinalize(this);
      }
    }

    /// <summary>Shared client disposal path used by explicit disposal, node disposal, and finalization.</summary>
    private void Dispose(bool disposing)
    {
      Exception disposeException = null;
      lock (mutex)
      {
        if (disposed)
        {
          return;
        }

        // Complete pending calls so callers do not wait forever after shutdown/reconnect.
        lock (Requests)
        {
          foreach (var source in Requests.Values)
          {
            source.Item1.TrySetException(new ObjectDisposedException("client has been disposed"));
          }
          Requests.Clear();
        }

        try
        {
          if (!node.IsDisposed)
          {
            int ret = NativeRcl.rcl_client_fini(ref clientHandle, ref node.nodeHandle);
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
            if (clientOptions != IntPtr.Zero)
            {
              NativeRclInterface.rclcs_client_dispose_options(clientOptions);
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
            clientOptions = IntPtr.Zero;
            disposed = true;
          }
        }
      }

      if (disposing)
      {
        logger.LogInfo("Client destroyed");
        if (disposeException != null)
        {
          throw disposeException;
        }
      }
    }

    /// <inheritdoc/>
    public bool IsServiceAvailable()
    {
      bool available = false;
      Utils.CheckReturnEnum(NativeRcl.rcl_service_server_is_available(
        ref node.nodeHandle,
        ref clientHandle,
        ref available
      ));
      return available;
    }

    /// <inheritdoc/>
    public void TakeMessage()
    {
      MessageInternals msg = null;
      rcl_rmw_request_id_t requestHeader = default(rcl_rmw_request_id_t);
      RCLReturnEnum ret;
      lock (mutex)
      {
        if (disposed || !Ros2cs.Ok())
        {
          return;
        }

        msg = CreateResponseMessage();
        ret = (RCLReturnEnum)NativeRcl.rcl_take_response(
          ref clientHandle,
          ref requestHeader,
          msg.Handle
        );
      }

      if (ret == RCLReturnEnum.RCL_RET_CLIENT_TAKE_FAILED)
      {
        ((IDisposable)msg).Dispose();
        return;
      }

      if (ret != RCLReturnEnum.RCL_RET_OK)
      {
        ((IDisposable)msg).Dispose();
        Utils.CheckReturnEnum((int)ret);
        return;
      }

      bool processed = ProcessResponse(requestHeader.sequence_number, msg);
      if (!processed)
      {
        ((IDisposable)msg).Dispose();
      }
    }

    /// <summary>Create a response message and validate its native-message interface.</summary>
    private MessageInternals CreateResponseMessage()
    {
      O msg = new O();
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
    /// Populates managed fields with native values and finishes the corresponding <see cref="Task"/>
    /// </summary>
    /// <param name="msg">Message that will be populated and used as the task result</param>
    /// <param name="sequence_number">sequence number received when sending the Request</param>
    /// <returns>True when the response matched a pending request and now owns the message.</returns>
    private bool ProcessResponse(long sequence_number, MessageInternals msg)
    {
      bool exists = false;
      (TaskCompletionSource<O>, Task<O>) source = default((TaskCompletionSource<O>, Task<O>));
      lock (Requests)
      {
        if (Requests.TryGetValue(sequence_number, out source))
        {
          exists = true;
          Requests.Remove(sequence_number);
        }
      }
      if (exists)
      {
        msg.ReadNativeMessage();
        source.Item1.SetResult((O)msg);
        return true;
      }
      else
      {
        Debug.Print("received unknown sequence number or got disposed");
        return false;
      }
    }

    /// <summary>
    /// Send a Request to the Service
    /// </summary>
    /// <param name="msg">Message to be send</param>
    /// <returns>sequence number of the Request</returns>
    private long SendRequest(I msg)
    {
      long sequence_number = default(long);
      MessageInternals msgInternals = MessageTypeSupportHelper.AsMessageInternals(msg, nameof(msg));
      msgInternals.WriteNativeMessage();
      Utils.CheckReturnEnum(
        NativeRcl.rcl_send_request(
          ref clientHandle,
          msgInternals.Handle,
          ref sequence_number
        )
      );
      return sequence_number;
    }

    /// <summary>
    /// Associate a task with a sequence number
    /// </summary>
    /// <param name="source">source used to controll the <see cref="Task"/></param>
    /// <param name="sequence_number">sequence number received when sending the Request</param>
    /// <returns>The associated task.</returns>
    private Task<O> RegisterSource(TaskCompletionSource<O> source, long sequence_number)
    {
      Task<O> task = source.Task;
      lock (Requests)
      {
        Requests.Add(sequence_number, (source, task));
      }
      return task;
    }

    /// <inheritdoc/>
    public O Call(I msg)
    {
      if (Ros2cs.IsInSpinCallback)
      {
        // A blocking call from a spin callback would wait for the same spin loop that is currently busy.
        throw new InvalidOperationException("Synchronous Client.Call cannot be used from a spin callback; use CallAsync instead.");
      }

      var task = CallAsync(msg);
      task.Wait();
      return task.Result;
    }

    /// <inheritdoc/>
    public Task<O> CallAsync(I msg)
    {
      return CallAsync(msg, TaskCreationOptions.None);
    }

    /// <inheritdoc/>
    public Task<O> CallAsync(I msg, TaskCreationOptions options)
    {
      TaskCompletionSource<O> source;
      lock (mutex)
      {
          if (!Ros2cs.Ok() || disposed)
          {
            throw new InvalidOperationException("Cannot call as the class is already disposed or shutdown was called");
          }
          if (node.IsDisposed)
          {
            throw new InvalidOperationException("Cannot call as the owning node is already disposed");
          }
          long sequence_number = SendRequest(msg);
          source = new TaskCompletionSource<O>(options);
          return RegisterSource(source, sequence_number);
      }
    }

    /// <inheritdoc/>
    public bool Cancel(Task task)
    {
      var pair = default(KeyValuePair<long, (TaskCompletionSource<O>, Task<O>)>);
      try
      {
        lock(this.Requests)
        {
          pair = this.Requests.First(entry => entry.Value.Item2 == task);
          this.Requests.Remove(pair.Key);
        }
      }
      catch (InvalidOperationException)
      {
        return false;
      }
      return pair.Value.Item1.TrySetCanceled();
    }

    /// <summary>
    /// Wrapper to avoid exposing <see cref="TaskCompletionSource"/> to users.
    /// </summary>
    /// <remarks>
    /// The locking used is required because the user may access the view while <see cref="Client.TakeMessage"/> is running.
    /// </remarks>
    private class PendingTasksView : IReadOnlyDictionary<long, Task<O>>, IReadOnlyDictionary<long, Task>
    {
      public Task<O> this[long key]
      {
        get
        {
          lock (this.Requests)
          {
            return this.Requests[key].Item2;
          }
        }
      }

      Task IReadOnlyDictionary<long, Task>.this[long key]
      {
        get { return this[key]; }
      }

      public IEnumerable<long> Keys
      {
        get
        {
          lock (this.Requests)
          {
            return this.Requests.Keys.ToArray();
          }
        }
      }

      public IEnumerable<Task<O>> Values
      {
        get
        {
          lock (this.Requests)
          {
            return this.Requests.Values.Select(value => value.Item2).ToArray();
          }
        }
      }

      IEnumerable<Task> IReadOnlyDictionary<long, Task>.Values
      {
        get { return this.Values; }
      }

      public int Count
      {
        get
        {
          lock (this.Requests)
          {
            return this.Requests.Count;
          }
        }
      }

      private readonly IReadOnlyDictionary<long, (TaskCompletionSource<O>, Task<O>)> Requests;

      public PendingTasksView(IReadOnlyDictionary<long, (TaskCompletionSource<O>, Task<O>)> requests)
      {
        this.Requests = requests;
      }

      public bool ContainsKey(long key)
      {
        lock (this.Requests)
        {
          return this.Requests.ContainsKey(key);
        }
      }

      public bool TryGetValue(long key, out Task<O> value)
      {
        bool success = false;
        (TaskCompletionSource<O>, Task<O>) source = default((TaskCompletionSource<O>, Task<O>));
        lock (this.Requests)
        {
          success = this.Requests.TryGetValue(key, out source);
        }
        value = source.Item2;
        return success;
      }

      bool IReadOnlyDictionary<long, Task>.TryGetValue(long key, out Task value)
      {
        bool success = this.TryGetValue(key, out var task);
        value = task;
        return success;
      }

      public IEnumerator<KeyValuePair<long, Task<O>>> GetEnumerator()
      {
        lock (this.Requests)
        {
          return this.Requests
            .Select(pair => new KeyValuePair<long, Task<O>>(pair.Key, pair.Value.Item2))
            .ToArray()
            .AsEnumerable()
            .GetEnumerator();
        }
      }

      IEnumerator IEnumerable.GetEnumerator()
      {
        return this.GetEnumerator();
      }

      IEnumerator<KeyValuePair<long, Task>> IEnumerable<KeyValuePair<long, Task>>.GetEnumerator()
      {
        lock (this.Requests)
        {
          return this.Requests
            .Select(pair => new KeyValuePair<long, Task>(pair.Key, pair.Value.Item2))
            .ToArray()
            .AsEnumerable()
            .GetEnumerator();
        }
      }
    }
  }
}
