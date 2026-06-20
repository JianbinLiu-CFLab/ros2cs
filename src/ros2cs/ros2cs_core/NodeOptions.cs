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
// - Added opt-in node creation options for lightweight runtime profiles.
// - Kept defaults compatible with existing ros2cs node behavior.

namespace ROS2
{
  /// <summary>Optional settings applied when creating a ros2cs node.</summary>
  /// <remarks>
  /// Defaults preserve existing ros2cs node behavior. Set individual options only when a
  /// lightweight runtime profile explicitly accepts the corresponding ROS-visible tradeoff.
  /// </remarks>
  public sealed class NodeOptions
  {
    /// <summary>Create a new options instance with the default ros2cs node behavior.</summary>
    public static NodeOptions Default
    {
      get { return new NodeOptions(); }
    }

    /// <summary>Whether rcl should create rosout logging support for the node.</summary>
    public bool EnableRosout { get; set; } = true;
  }
}
