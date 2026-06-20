// Copyright 2021 Robotec.ai
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Kept benchmark disposal behavior explicit while auditing ros2cs common helpers.
// - Made benchmark disposal idempotent under concurrent callers.
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
using System.Diagnostics;
using System.Threading;

namespace ROS2
{
  /// <summary>
  /// Utility class for simple wall-clock elapsed time measurement.
  /// </summary>
  /// <remarks>
  /// Uses <see cref="Stopwatch"/>, so elapsed time includes scheduler delay and waits; it is not CPU time.
  /// Logged ticks are Stopwatch ticks and must be divided by Stopwatch.Frequency to get seconds.
  /// </remarks>
  /// <code>
  /// /* example use */
  /// using (var bench = new Benchmark("name_to_show_in_logs"))
  /// {
  ///   [code to benchmark]
  /// }
  /// </code>
  public class Benchmark : IExtendedDisposable
  {
    private readonly Stopwatch timer = new Stopwatch();
    private readonly string benchmarkName;

    /// <summary>Whether this benchmark has already stopped and logged its elapsed time.</summary>
    public bool IsDisposed { get { return disposed != 0; } }

    // Interlocked disposal sentinel: 0 means active, 1 means disposed.
    private int disposed = 0;

    /// <summary>Start measuring elapsed time for the named benchmark scope.</summary>
    /// <param name="benchmarkName">Name included in the DEBUG log emitted on disposal.</param>
    public Benchmark(string benchmarkName)
    {
      this.benchmarkName = benchmarkName;
      timer.Start();
    }

    /// <summary>Stop the timer once and log elapsed wall-clock ticks and milliseconds at DEBUG level.</summary>
    public void Dispose()
    {
      if (Interlocked.Exchange(ref disposed, 1) == 0)
      {
        timer.Stop();
        Ros2csLogger.GetInstance().LogDebug(
          () => $"{benchmarkName} {timer.ElapsedTicks} ticks ({timer.ElapsedMilliseconds} ms)");
      }
    }
  }
}  // namespace ROS2
