// Copyright 2019-2021 Robotec.ai
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Audited client example metadata during Jazzy/.NET maintenance.
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
using System.Threading;
using ROS2;

namespace Examples
{
  /// <summary> A simple service client class to illustrate Ros2cs in action </summary>
  public class ROS2Client
  {
    public static void Main(string[] args)
    {
      Console.WriteLine("Client start");
      using var runtime = new ROS2ExampleRuntime();
      INode node = Ros2cs.CreateNode("client");
      using Client<example_interfaces.srv.AddTwoInts_Request, example_interfaces.srv.AddTwoInts_Response> my_client = node.CreateClient<example_interfaces.srv.AddTwoInts_Request, example_interfaces.srv.AddTwoInts_Response>("add_two_ints");

      using var msg = new example_interfaces.srv.AddTwoInts_Request();
      msg.A = 7;
      msg.B = 2;

      DateTime waitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
      while (!my_client.IsServiceAvailable())
      {
        if (DateTime.UtcNow >= waitDeadline)
        {
          throw new TimeoutException("Timed out waiting for add_two_ints service.");
        }
        Thread.Sleep(TimeSpan.FromSeconds(0.25));
      }

      using example_interfaces.srv.AddTwoInts_Response rsp = my_client.Call(msg);
      Console.WriteLine("Sum = " + rsp.Sum);

      Console.WriteLine("Client shutdown");
    }
  }
}
