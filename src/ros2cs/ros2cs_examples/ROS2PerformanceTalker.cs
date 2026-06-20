// Copyright 2019-2021 Robotec.ai
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Audited performance talker example metadata during Jazzy/.NET maintenance.
// - Added explicit disposal for nested PointField message wrappers.
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
  /// <summary> A talker class meant to gauge performance of Ros2cs </summary>
  public class ROS2PerformanceTalker
  {
    private const uint PointFieldBytes = 16; // Four float32 fields: x, y, z, intensity.
    private const byte PointFieldFloat32 = 7;
    private const float DummyX = 1.0f;
    private const float DummyY = 2.0f;
    private const float DummyZ = 3.0f;
    private const float DummyIntensity = 100.0f;

    private static void AssignField(ref sensor_msgs.msg.PointField pf, string n, uint off, byte dt, uint count)
    {
      pf.Name = n;
      pf.Offset = off;
      pf.Datatype = dt;
      pf.Count = count;
    }

    private static sensor_msgs.msg.PointCloud2 PrepMessage(int messageSize)
    {
      uint count = (uint)messageSize; //point per message
      uint rowSize = count * PointFieldBytes;
      sensor_msgs.msg.PointCloud2 message = new sensor_msgs.msg.PointCloud2()
      {
        Height = 1,
        Width = count,
        Is_bigendian = false,
        Is_dense = true,
        Point_step = PointFieldBytes,
        Row_step = rowSize,
        Data = new byte[rowSize * 1]
      };
      uint pointFieldCount = 4;
      message.Fields = new sensor_msgs.msg.PointField[pointFieldCount];
      for (int i = 0; i < pointFieldCount; ++i)
      {
        message.Fields[i] = new sensor_msgs.msg.PointField();
      }

      AssignField(ref message.Fields[0], "x", 0, PointFieldFloat32, 1);
      AssignField(ref message.Fields[1], "y", 4, PointFieldFloat32, 1);
      AssignField(ref message.Fields[2], "z", 8, PointFieldFloat32, 1);
      AssignField(ref message.Fields[3], "intensity", 12, PointFieldFloat32, 1);
      float[] pointsArray = new float[count * message.Fields.Length];

      var floatIndex = 0;
      for (int i = 0; i < count; ++i)
      {
        // Dummy point values keep payload generation deterministic; latency is the benchmark target.
        pointsArray[floatIndex++] = DummyX;
        pointsArray[floatIndex++] = DummyY;
        pointsArray[floatIndex++] = DummyZ;
        pointsArray[floatIndex++] = DummyIntensity;
      }
      System.Buffer.BlockCopy(pointsArray, 0, message.Data, 0, message.Data.Length);
      message.SetHeaderFrame("pc");
      return message;
    }

    /// <summary>
    /// Dispose caller-supplied PointField wrappers assigned to the reusable PointCloud2 example message.
    /// Generated message disposal releases only direct nested fields and read-owned sequence elements.
    /// </summary>
    private static void DisposePointFields(sensor_msgs.msg.PointCloud2 message)
    {
      if (message?.Fields == null)
      {
        return;
      }

      foreach (sensor_msgs.msg.PointField field in message.Fields)
      {
        field?.Dispose();
      }
    }

    public static void Main(string[] args)
    {
      using var runtime = new ROS2ExampleRuntime();
      using var clock = new Clock();
      using INode node = Ros2cs.CreateNode("perf_talker");
      using var qos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);
      using IPublisher<sensor_msgs.msg.PointCloud2> pc_pub = node.CreatePublisher<sensor_msgs.msg.PointCloud2>("perf_chatter", qos);

      Console.WriteLine("Enter PC2 data size: ");
      int messageSize = Convert.ToInt32(Console.ReadLine());
      using sensor_msgs.msg.PointCloud2 msg = PrepMessage(messageSize);
      // System.Random rand = new System.Random();

      try
      {
        while (Ros2cs.Ok())
        {
          var nowTime = clock.Now;
          msg.UpdateHeaderTime(nowTime.sec, nowTime.nanosec);

          // Remove this benchmark if you want to measure maximum throughput for smallest messages
          using (var bench = new Benchmark("Publish"))
          {
            // If we want to test changing sizes:
            // msg = PrepMessage(rand.Next() / 1000);
            pc_pub.Publish(msg);
          }
        }
      }
      finally
      {
        DisposePointFields(msg);
      }
    }
  }
}
