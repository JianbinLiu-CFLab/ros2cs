// Copyright 2019-2021 Robotec.ai
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
  /// <summary>Basic message interface, exposing disposal for native-backed generated messages.</summary>
  /// <remarks>
  /// The caller that creates a message owns disposal. Generated messages must be explicitly disposed
  /// and must not declare finalizers that call native message destroy functions during process or
  /// Unity domain teardown.
  /// </remarks>
  public interface Message : IExtendedDisposable
  {
  }

  /// <summary>Convenience interface for generated messages that expose a standard ROS 2 header.</summary>
  public interface MessageWithHeader : Message
  {
    /// <summary>Set the header frame id string.</summary>
    /// <param name="frameID">Frame identifier written to the message header.</param>
    void SetHeaderFrame(string frameID);

    /// <summary>Get the header frame id string.</summary>
    /// <returns>The current frame identifier from the message header.</returns>
    string GetHeaderFrame();

    /// <summary>Update the split ROS 2 header timestamp.</summary>
    /// <param name="sec">Whole seconds component of <c>builtin_interfaces/Time</c>.</param>
    /// <param name="nanosec">Sub-second nanoseconds component of <c>builtin_interfaces/Time</c>.</param>
    void UpdateHeaderTime(int sec, uint nanosec);
  }
}  // namespace ROS2
