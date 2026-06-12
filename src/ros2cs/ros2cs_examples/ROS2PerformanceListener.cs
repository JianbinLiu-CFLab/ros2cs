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
// - Fixed average/stddev calculations before the sample queue reaches capacity.
// - Replaced callback-side process exit with a normal spin loop shutdown path.

using System;
using ROS2;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;

namespace Examples
{
  public class FixedSizedQueue : ConcurrentQueue<double>
  {
    public struct InfoStruct
    {
      public double stdDev;
      public double mean;
    }

    private readonly object syncObject = new object();
    private InfoStruct result = new InfoStruct();

    public int Size { get; private set; }

    public FixedSizedQueue(int size)
    {
      Size = size;
    }

    public double Avg()
    {
      double sum = 0.0;
      foreach (double diff in this)
      {
        sum += diff;
      }
      return this.Count == 0 ? 0.0 : (double)(sum/this.Count);
    }

    public InfoStruct MeanAndStdDev()
    {
      var variance = 0.0;
      lock (syncObject)
      {
        var mean = this.Avg();
        foreach (double diff in this)
        {
          variance += (diff - mean) * (diff - mean);
        }
        result.mean = mean;
        result.stdDev = this.Count <= 1 ? 0.0 : Math.Sqrt((double)(1.0/(this.Count-1)) * variance);
        return result;
      }
    }

    public new void Enqueue(double obj)
    {
      base.Enqueue(obj);
      lock (syncObject)
      {
        while (base.Count > Size)
        {
          double outObj;
          base.TryDequeue(out outObj);
        }
      }
    }
  }

  /// <summary> A listener class meant to gauge performance of Ros2cs </summary>
  public class ROS2PerformanceListener
  {
    public static void Main(string[] args)
    {
      using var runtime = new ROS2ExampleRuntime();
      using var clock = new Clock();
      using INode node = Ros2cs.CreateNode("perf_listener");
      Console.WriteLine("Enter sample size: ");
      int sampleSize = Convert.ToInt32(Console.ReadLine());
      Console.WriteLine("Waiting for {0} messages...", sampleSize);
      FixedSizedQueue queue = new FixedSizedQueue(sampleSize);

      RosTime timeStamp = new RosTime();
      int counter = 0;
      int done = 0;

      using (QualityOfServiceProfile qos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA))
      {
        using ISubscription<sensor_msgs.msg.PointCloud2> chatter_sub = node.CreateSubscription<sensor_msgs.msg.PointCloud2>(
          "perf_chatter",
          msg =>
          {
            RosTime timeNow = clock.Now;
            timeStamp.nanosec = msg.Header.Stamp.Nanosec;
            timeStamp.sec = msg.Header.Stamp.Sec;
            var diff = timeNow.Seconds - timeStamp.Seconds;

            queue.Enqueue(diff);
            counter++;

            if (counter == queue.Size)
            {
              counter = 0;
              var result = queue.MeanAndStdDev();
              Console.WriteLine("Latency of sample size {0} - avg: {1:F6}s, std dev: {2:F10}s", sampleSize, result.mean, result.stdDev);
              Console.WriteLine("PERFORMANCE_LISTENER_COMPLETE sample_size={0} avg_s={1:F6} stddev_s={2:F10}", sampleSize, result.mean, result.stdDev);
              Volatile.Write(ref done, 1);
            }
          },
          qos);

        while (Volatile.Read(ref done) == 0 && Ros2cs.Ok())
        {
          Ros2cs.SpinOnce(node, 0.1);
        }
      }
    }
  }
}
