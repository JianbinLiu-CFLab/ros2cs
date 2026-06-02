// Copyright 2019-2021 Robotec.ai
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Audited listener example metadata during Jazzy/.NET maintenance.
// - Made node lifetime explicit instead of relying only on global shutdown.
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
using ROS2;

namespace Examples
{
  /// <summary> A simple listener class to illustrate Ros2cs in action </summary>
  public class ROS2Listener
  {
    public static void Main(string[] args)
    {
      using var runtime = new ROS2ExampleRuntime();
      using INode node = Ros2cs.CreateNode("listener");

      using ISubscription<std_msgs.msg.String> chatter_sub = node.CreateSubscription<std_msgs.msg.String>(
        "chatter", msg => Console.WriteLine("I heard: [" + msg.Data + "]"));

      while (Ros2cs.Ok())
      {
        Ros2cs.SpinOnce(node, 0.1);
      }
    }
  }
}
