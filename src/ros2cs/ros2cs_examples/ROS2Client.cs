// Copyright 2019-2021 Robotec.ai
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Audited client example metadata during Jazzy/.NET maintenance.
// - Added a background spin loop so synchronous Call(timeout) can receive responses.
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
using System.Threading.Tasks;
using ROS2;

namespace Examples
{
  /// <summary> A simple service client class to illustrate Ros2cs in action </summary>
  public class ROS2Client
  {
    private static readonly TimeSpan ServiceWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ServiceCallTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SpinShutdownTimeout = TimeSpan.FromSeconds(1);

    public static void Main(string[] args)
    {
      Console.WriteLine("Client start");
      using var runtime = new ROS2ExampleRuntime();
      using INode node = Ros2cs.CreateNode("client");
      using Client<example_interfaces.srv.AddTwoInts_Request, example_interfaces.srv.AddTwoInts_Response> my_client = node.CreateClient<example_interfaces.srv.AddTwoInts_Request, example_interfaces.srv.AddTwoInts_Response>("add_two_ints");

      using var msg = new example_interfaces.srv.AddTwoInts_Request();
      msg.A = 7;
      msg.B = 2;

      // Bound service discovery so the example fails loudly when ros2cs_service is not running.
      if (!my_client.TryWaitForService(ServiceWaitTimeout))
      {
        throw new TimeoutException("Timed out waiting for add_two_ints service.");
      }

      bool spinDone = false;
      Task spinTask = Task.Run(() =>
      {
        while (!Volatile.Read(ref spinDone))
        {
          // The synchronous Call below needs this spin loop to receive the service response.
          Ros2cs.SpinOnce(node, 0.1);
        }
      });

      try
      {
        // Call is synchronous; dispose the generated response because it owns a native message handle.
        using example_interfaces.srv.AddTwoInts_Response rsp = my_client.Call(msg, ServiceCallTimeout);
        Console.WriteLine("Sum = " + rsp.Sum);
      }
      finally
      {
        Volatile.Write(ref spinDone, true);
        spinTask.Wait(SpinShutdownTimeout);
      }

      Console.WriteLine("Client shutdown");
    }
  }
}
