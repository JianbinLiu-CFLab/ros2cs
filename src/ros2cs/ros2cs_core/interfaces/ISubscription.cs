// Copyright 2019-2021 Robotec.ai
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Audited subscription interface contracts after take-message ownership fixes.
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

namespace ROS2
{
  /// <summary> Non-generic base interface for all subscriptions </summary>
  /// <remarks> Use <see cref="INode.CreateSubscription{T}(string, Action{T}, QualityOfServiceProfile)"/> to construct. </remarks>
  public interface ISubscriptionBase : IExtendedDisposable
  {
    /// <remarks>
    /// When a message is successfully taken, the subscription callback receives a message wrapper
    /// owned by ros2cs. Callers must not retain or dispose that callback argument; ros2cs disposes
    /// it after the callback returns.
    /// Implementations must check disposed state and <see cref="Ros2cs.Ok"/> under their own mutex
    /// before any native take call because shutdown can invalidate a spin snapshot before callbacks run.
    /// </remarks>
    // Internal spin entry point; kept on the public interface for compatibility.
    void TakeMessage();

    /// <summary> Topic name used when calling <see cref="INode.CreateSubscription{T}(string, Action{T}, QualityOfServiceProfile)"/>. </summary>
    string Topic {get;}

    // Internal wait-set handle; kept on the public interface for compatibility.
    rcl_subscription_t Handle {get;}

    /// <summary> subscription mutex for internal use </summary>
    object Mutex { get; }
  }

  /// <summary> Generic base interface for all subscriptions </summary>
  /// <remarks>The generic parameter records the managed message type accepted by the callback.</remarks>
  public interface ISubscription<T>: ISubscriptionBase where T: Message {}
}
