// Copyright 2021 Robotec.ai
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
  /// <summary>Extended disposable interface to expose object disposal state.</summary>
  /// <remarks>
  /// Implementations should document any thread-safety guarantees for <see cref="IsDisposed"/>.
  /// Callers must not assume that checking <see cref="IsDisposed"/> makes a later operation safe
  /// from races with another thread disposing the object.
  /// </remarks>
  public interface IExtendedDisposable : IDisposable
  {
    /// <summary>Whether this instance has been disposed.</summary>
    bool IsDisposed { get; }
  }

}
