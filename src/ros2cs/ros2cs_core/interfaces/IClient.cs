// Copyright 2019-2021 Robotec.ai
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Audited client interface contracts after lifecycle and timeout-path fixes.
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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ROS2
{
  /// <summary> Non-generic base interface for all clients </summary>
  /// <seealso cref="INode.CreateClient"/>
  public interface IClientBase: IExtendedDisposable
  {
    /// <summary>
    /// Tries to get a Response message from rcl/rmw layers
    /// </summary>
    /// <remarks>
    /// Marks the corresponding <see cref="Task"/> as finished if successful
    /// </remarks>
    // Internal spin entry point; kept on the public interface for compatibility.
    void TakeMessage();

    /// <summary>
    /// topic name which was used when calling <see cref="INode.CreateClient"/>
    /// </summary>
    string Topic {get;}

    /// <summary>
    /// Requests which are pending for this client.
    /// </summary>
    IReadOnlyDictionary<long, Task> PendingRequests {get;}

    /// <summary>
    /// Remove a pending <see cref="Task"/> and cancel it.
    /// </summary>
    /// <remarks>
    /// Tasks are automatically removed on completion and have to be removed only when canceled.
    /// </remarks>
    /// <param name="task">Task to be removed.</param>
    /// <returns>Whether the Task was removed successfully.</returns>
    bool Cancel(Task task);

    // Internal wait-set handle; kept on the public interface for compatibility.
    rcl_client_t Handle {get;}

    /// <summary> client mutex for internal use </summary>
    object Mutex { get; }
  }

  /// <summary> Generic base interface for all clients </summary>
  /// <typeparam name="I">Message Type to be send</typeparam>
  /// <typeparam name="O">Message Type to be received</typeparam>
  /// <seealso cref="INode.CreateClient"/>
  public interface IClient<I, O>: IClientBase
      where I: Message
      where O: Message
  {
    /// <summary>
    /// Requests which are pending for this client.
    /// </summary>
    new IReadOnlyDictionary<long, Task<O>> PendingRequests {get;}

    /// <summary>
    /// Check if the service to be called is available
    /// </summary>
    /// <returns><see cref="true"/> if the service is available</returns>
    bool IsServiceAvailable();

    /// <summary>
    /// Wait up to the given timeout for the service to become available
    /// </summary>
    /// <remarks>Polls <see cref="IsServiceAvailable"/> and returns false on timeout.</remarks>
    /// <param name="timeout">Maximum time to wait for service graph discovery</param>
    /// <returns><see cref="true"/> if the service became available before timeout</returns>
    bool TryWaitForService(TimeSpan timeout);

    /// <summary>
    /// Send a Request to a Service and wait for a Response
    /// </summary>
    /// <remarks>The provided message can be modified or disposed after this call</remarks>
    /// <param name="msg">Message to be send</param>
    /// <returns>Response of the Service</returns>
    O Call(I msg);

    /// <summary>
    /// Send a Request to a Service and wait up to the given timeout for a Response
    /// </summary>
    /// <remarks>The pending request is canceled when the timeout elapses</remarks>
    /// <param name="msg">Message to be send</param>
    /// <param name="timeout">Maximum time to wait for a response</param>
    /// <returns>Response of the Service</returns>
    O Call(I msg, TimeSpan timeout);

    /// <summary>
    /// Send a Request to a Service and wait for a Response asynchronously
    /// </summary>
    /// <param name="msg">Message to be send</param>
    /// <returns><see cref="Task"/> representing the Response of the Service</returns>
    Task<O> CallAsync(I msg);

    /// <inheritdoc cref="CallAsync(I)"/>
    /// <param name="options">Options used when creating the <see cref="Task"/></param>
    Task<O> CallAsync(I msg, TaskCreationOptions options);
  }
}
