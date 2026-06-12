// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Kept benchmark disposal behavior explicit while auditing ros2cs common helpers.
// - Made benchmark disposal idempotent under concurrent callers.

using System;
using System.Diagnostics;
using System.Threading;

namespace ROS2
{
  /// <summary> An utility class for simple code block execution time measurement </summary>
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

    public bool IsDisposed { get { return disposed != 0; } }
    private int disposed = 0;

    public Benchmark(string benchmarkName)
    {
      this.benchmarkName = benchmarkName;
      timer.Start();
    }

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
