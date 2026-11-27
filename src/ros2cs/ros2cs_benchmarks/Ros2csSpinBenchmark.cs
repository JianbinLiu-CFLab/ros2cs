// Copyright 2026 Jianbin Liu.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using ROS2;

namespace Ros2csSpinBenchmarks
{
  internal static class Ros2csSpinBenchmark
  {
    private const int SchemaVersion = 2;
    private const string SpinFallbackEnvironmentVariable = "ROS2CS_SPIN_FALLBACK";
    private const double IdleSpinTimeoutSeconds = 0.0;
    private const double ActiveSpinTimeoutSeconds = 0.01;
    private const double DeliveryTimeoutSeconds = 5.0;
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(5);

    public static int Main(string[] args)
    {
      BenchmarkArguments arguments;
      try
      {
        arguments = BenchmarkArguments.Parse(args);
      }
      catch (ArgumentException exception)
      {
        Console.Error.WriteLine(exception.Message);
        PrintUsage();
        return 2;
      }

      if (arguments.ShowHelp)
      {
        PrintUsage();
        return 0;
      }

      BenchmarkResult result = CreateBaseResult(arguments);
      string previousFallback = Environment.GetEnvironmentVariable(SpinFallbackEnvironmentVariable);
      bool initialized = false;
      try
      {
        string environmentDistro = Environment.GetEnvironmentVariable("ROS_DISTRO");
        if (!String.Equals(arguments.Distro, environmentDistro, StringComparison.OrdinalIgnoreCase))
        {
          result.Status = "NOT_RUN";
          result.StatusReason = "ROS_DISTRO is '" + (environmentDistro ?? String.Empty) +
            ", requested '" + arguments.Distro + "'";
          return WriteResult(result, arguments.OutputPath);
        }

        Environment.SetEnvironmentVariable(
          SpinFallbackEnvironmentVariable,
          arguments.Mode == "direct" ? "direct" : "waitset");

        Ros2cs.Init();
        initialized = true;
        result.Rmw = Ros2cs.GetRMWImplementation();
        result.EffectiveMode = Ros2cs.ActiveSpinModeForTests;
        if (!String.Equals(result.EffectiveMode, arguments.Mode, StringComparison.Ordinal))
        {
          throw new InvalidOperationException(
            "Requested mode '" + arguments.Mode + "' resolved to '" + result.EffectiveMode + "'");
        }

        BenchmarkRun run = ExecuteBenchmark(arguments);
        result.Metrics = run.Metrics;
        result.Metrics.ProcessPrivateMemoryStartBytes = run.ProcessPrivateMemoryStartBytes;
        result.Status = "PASS";
      }
      catch (Exception exception)
      {
        result.Status = initialized ? "FAIL" : "NOT_RUN";
        result.StatusReason = exception.ToString();
      }
      finally
      {
        if (Ros2cs.Ok())
        {
          Stopwatch shutdownTimer = Stopwatch.StartNew();
          try
          {
            Ros2cs.Shutdown();
            result.Metrics.ShutdownLatencyMs = ToMilliseconds(shutdownTimer.ElapsedTicks);
          }
          catch (Exception exception)
          {
            result.Status = "FAIL";
            result.StatusReason = exception.ToString();
          }
        }

        result.Metrics.ProcessPrivateMemoryEndBytes = GetProcessPrivateMemoryBytes();
        Environment.SetEnvironmentVariable(SpinFallbackEnvironmentVariable, previousFallback);
      }

      if (result.Status == "PASS")
      {
        Console.WriteLine(
          "ROS2CS_SPIN_BENCHMARK_PASS distro={0} mode={1} entities={2} load={3} throughput={4:F3}",
          result.Distro,
          result.EffectiveMode,
          result.EntityCount,
          result.LoadProfile,
          result.Metrics.ThroughputMessagesPerSecond);
      }

      return WriteResult(result, arguments.OutputPath);
    }

    private static int EstimateSpinDurationCapacity(BenchmarkArguments arguments)
    {
      long activeEntityCount = arguments.LoadProfile == "idle"
        ? 0
        : arguments.LoadProfile == "few-active"
          ? Math.Max(1, arguments.EntityCount / 100)
          : arguments.EntityCount;
      long publishingIterations = arguments.LoadProfile == "low"
        ? (arguments.Iterations + 9L) / 10L
        : arguments.Iterations;
      long expectedDrainSamples = activeEntityCount * publishingIterations;
      long capacity = Math.Max(0L, arguments.Iterations) + expectedDrainSamples;
      return capacity >= int.MaxValue ? int.MaxValue : (int)capacity;
    }

    private static BenchmarkRun ExecuteBenchmark(BenchmarkArguments arguments)
    {
      long processPrivateMemoryStartBytes = GetProcessPrivateMemoryBytes();
      var state = new BenchmarkState(
        arguments.EntityCount,
        arguments.LoadProfile,
        EstimateSpinDurationCapacity(arguments));
      try
      {
        using INode node = Ros2cs.CreateNode("ros2cs_spin_benchmark_node");
        CreateEntities(node, state);
        WaitForEntityDiscovery(node, state);

        RunSpinIterations(node, state, arguments.WarmupIterations);
        DrainMessages(node, state);
        state.ResetMeasurement();

        long wallStart = Stopwatch.GetTimestamp();
        long cpuStart = Process.GetCurrentProcess().TotalProcessorTime.Ticks;
        long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        RunSpinIterations(node, state, arguments.Iterations);
        DrainMessages(node, state);
        long allocatedEnd = GC.GetAllocatedBytesForCurrentThread();
        long wallTicks = Stopwatch.GetTimestamp() - wallStart;
        long cpuEnd = Process.GetCurrentProcess().TotalProcessorTime.Ticks;

        BenchmarkMetrics metrics = new BenchmarkMetrics();
        long spinCalls = Interlocked.Read(ref state.SpinCalls);
        long drainSpinCalls = Interlocked.Read(ref state.DrainSpinCalls);
        metrics.SpinCalls = spinCalls;
        metrics.DrainSpinCalls = drainSpinCalls;
        metrics.AllocationBytesPerSpin = spinCalls == 0
          ? 0.0
          : (double)Math.Max(0L, Interlocked.Read(ref state.SpinAllocatedBytes)) / spinCalls;
        metrics.WorkloadAllocatedBytes = Math.Max(0L, allocatedEnd - allocatedStart);
        metrics.CpuSeconds = new TimeSpan(cpuEnd - cpuStart).TotalSeconds;
        double wallSeconds = ToSeconds(wallTicks);
        metrics.CpuPercent = wallSeconds <= 0.0
          ? 0.0
          : metrics.CpuSeconds / wallSeconds / Environment.ProcessorCount * 100.0;
        metrics.ThroughputMessagesPerSecond = wallSeconds <= 0.0
          ? 0.0
          : (double)Interlocked.Read(ref state.ReceivedMessages) / wallSeconds;

        List<double> latencySamples;
        lock (state.LatencyLock)
        {
          latencySamples = state.LatenciesMs.ToList();
        }
        metrics.LatencyP50Ms = Percentile(latencySamples, 50.0);
        metrics.LatencyP95Ms = Percentile(latencySamples, 95.0);
        metrics.LatencyP99Ms = Percentile(latencySamples, 99.0);

        List<double> spinDurationSamples;
        lock (state.SpinDurationLock)
        {
          spinDurationSamples = state.SpinDurationMs.ToList();
        }
        metrics.SpinDurationSampleCount = spinDurationSamples.Count;
        metrics.SpinDurationP50Ms = Percentile(spinDurationSamples, 50.0);
        metrics.SpinDurationP95Ms = Percentile(spinDurationSamples, 95.0);
        metrics.SpinDurationP99Ms = Percentile(spinDurationSamples, 99.0);

        MeasureCreateRemove(node, metrics);
        return new BenchmarkRun(metrics, processPrivateMemoryStartBytes);
      }
      finally
      {
        foreach (std_msgs.msg.Int64 message in state.Messages)
        {
          message.Dispose();
        }
      }
    }

    private static void CreateEntities(INode node, BenchmarkState state)
    {
      for (int i = 0; i < state.EntityCount; i++)
      {
        int entityIndex = i;
        string topic = "/ros2cs_benchmark_topic_" + i;
        state.Subscriptions.Add(node.CreateSubscription<std_msgs.msg.Int64>(
          topic,
          message =>
          {
            long key = MakeMessageKey(entityIndex, message.Data);
            if (state.PublishedAt.TryRemove(key, out long publishedAt))
            {
              double latencyMs = ToMilliseconds(Stopwatch.GetTimestamp() - publishedAt);
              lock (state.LatencyLock)
              {
                state.LatenciesMs.Add(latencyMs);
              }
              Interlocked.Increment(ref state.ReceivedMessages);
            }
          }));

        if (state.IsActive(entityIndex))
        {
          state.Publishers.Add(node.CreatePublisher<std_msgs.msg.Int64>(topic));
          state.Messages.Add(new std_msgs.msg.Int64());
          state.ActiveEntityIndices.Add(entityIndex);
          state.ActiveTopics.Add(topic);
        }
      }
    }

    private static void WaitForEntityDiscovery(INode node, BenchmarkState state)
    {
      foreach (string topic in state.ActiveTopics)
      {
        if (!node.TryWaitForSubscriber(topic, DiscoveryTimeout))
        {
          throw new InvalidOperationException(
            "Timed out waiting for subscriber discovery on topic '" + topic + "'");
        }
      }
    }

    private static void RunSpinIterations(INode node, BenchmarkState state, int iterations)
    {
      for (int i = 0; i < iterations; i++)
      {
        PublishActiveMessages(state, i);
        long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        long spinStart = Stopwatch.GetTimestamp();
        Ros2cs.SpinOnce(node, state.HasActivePublishers
          ? ActiveSpinTimeoutSeconds
          : IdleSpinTimeoutSeconds);
        long spinEnd = Stopwatch.GetTimestamp();
        long allocatedEnd = GC.GetAllocatedBytesForCurrentThread();
        state.RecordSpinDuration(spinEnd - spinStart);
        Interlocked.Add(
          ref state.SpinAllocatedBytes,
          Math.Max(0L, allocatedEnd - allocatedStart));
        Interlocked.Increment(ref state.SpinCalls);
      }
    }

    private static void DrainMessages(INode node, BenchmarkState state)
    {
      if (Interlocked.Read(ref state.PublishedMessages) == 0)
      {
        return;
      }

      long deadline = Stopwatch.GetTimestamp() +
        (long)(DeliveryTimeoutSeconds * Stopwatch.Frequency);
      while (Interlocked.Read(ref state.ReceivedMessages) <
        Interlocked.Read(ref state.PublishedMessages) &&
        Stopwatch.GetTimestamp() < deadline)
      {
        long spinStart = Stopwatch.GetTimestamp();
        Ros2cs.SpinOnce(node, ActiveSpinTimeoutSeconds);
        long spinEnd = Stopwatch.GetTimestamp();
        state.RecordSpinDuration(spinEnd - spinStart);
        Interlocked.Increment(ref state.DrainSpinCalls);
      }

      long receivedMessages = Interlocked.Read(ref state.ReceivedMessages);
      long publishedMessages = Interlocked.Read(ref state.PublishedMessages);
      if (receivedMessages != publishedMessages)
      {
        throw new InvalidOperationException("Message delivery incomplete: received " +
          receivedMessages + " of " + publishedMessages + " published messages");
      }
    }

    private static void PublishActiveMessages(BenchmarkState state, int iteration)
    {
      if (!state.ShouldPublish(iteration))
      {
        return;
      }

      long sequence = Interlocked.Increment(ref state.Sequence);
      for (int i = 0; i < state.Publishers.Count; i++)
      {
        int entityIndex = state.ActiveEntityIndices[i];
        long key = MakeMessageKey(entityIndex, sequence);
        state.PublishedAt[key] = Stopwatch.GetTimestamp();
        state.Messages[i].Data = sequence;
        state.Publishers[i].Publish(state.Messages[i]);
        Interlocked.Increment(ref state.PublishedMessages);
      }
    }

    private static void MeasureCreateRemove(INode node, BenchmarkMetrics metrics)
    {
      const int samples = 3;
      long createTicks = 0;
      long removeTicks = 0;
      for (int i = 0; i < samples; i++)
      {
        Stopwatch timer = Stopwatch.StartNew();
        ISubscription<std_msgs.msg.Int64> subscription =
          node.CreateSubscription<std_msgs.msg.Int64>(
            "/ros2cs_benchmark_create_remove_" + i,
            _ => { });
        createTicks += timer.ElapsedTicks;

        timer.Restart();
        if (!node.RemoveSubscription(subscription))
        {
          throw new InvalidOperationException("Benchmark subscription removal failed");
        }
        removeTicks += timer.ElapsedTicks;
      }

      metrics.CreateLatencyMs = ToMilliseconds(createTicks) / samples;
      metrics.RemoveLatencyMs = ToMilliseconds(removeTicks) / samples;
    }

    private static BenchmarkResult CreateBaseResult(BenchmarkArguments arguments)
    {
      return new BenchmarkResult
      {
        SchemaVersion = SchemaVersion,
        TimestampUtc = DateTimeOffset.UtcNow,
        Commit = Environment.GetEnvironmentVariable("ROS2CS_BENCHMARK_SOURCE_COMMIT") ??
          Environment.GetEnvironmentVariable("ROS2CS_BENCHMARK_COMMIT") ?? "unknown",
        SourceDirty = Environment.GetEnvironmentVariable("ROS2CS_BENCHMARK_SOURCE_DIRTY") ?? "unknown",
        SourceRoot = Environment.GetEnvironmentVariable("ROS2CS_BENCHMARK_SOURCE_ROOT") ?? "unknown",
        Distro = arguments.Distro,
        Mode = arguments.Mode,
        EntityCount = arguments.EntityCount,
        LoadProfile = arguments.LoadProfile,
        WarmupIterations = arguments.WarmupIterations,
        Iterations = arguments.Iterations,
        Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ??
          System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        CpuArchitecture =
          System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        LogicalProcessorCount = Environment.ProcessorCount,
        ShutdownScope = "context_after_benchmark_node_disposal",
        Metrics = new BenchmarkMetrics()
      };
    }

    private static int WriteResult(BenchmarkResult result, string outputPath)
    {
      try
      {
        string fullPath = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(fullPath);
        if (!String.IsNullOrEmpty(directory))
        {
          Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
          WriteIndented = true,
          PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        File.WriteAllText(fullPath, JsonSerializer.Serialize(result, options));
        Console.WriteLine("Benchmark result: " + fullPath);
        return result.Status == "FAIL" ? 1 : 0;
      }
      catch (Exception exception)
      {
        Console.Error.WriteLine("Could not write benchmark result: " + exception.Message);
        return 2;
      }
    }

    private static long MakeMessageKey(int entityIndex, long sequence)
    {
      return ((long)entityIndex << 32) | (sequence & UInt32.MaxValue);
    }

    private static long GetProcessPrivateMemoryBytes()
    {
      return Process.GetCurrentProcess().PrivateMemorySize64;
    }

    private static double? Percentile(List<double> values, double percentile)
    {
      if (values.Count == 0)
      {
        return null;
      }

      values.Sort();
      int index = (int)Math.Ceiling(percentile / 100.0 * values.Count) - 1;
      return values[Math.Clamp(index, 0, values.Count - 1)];
    }

    private static double ToSeconds(long stopwatchTicks)
    {
      return (double)stopwatchTicks / Stopwatch.Frequency;
    }

    private static double ToMilliseconds(long stopwatchTicks)
    {
      return ToSeconds(stopwatchTicks) * 1000.0;
    }

    private static void PrintUsage()
    {
      Console.WriteLine(
        "Usage: ros2cs_spin_benchmark --distro <distro> --mode <wait-set|direct> " +
        "--entities <count> --load <idle|low|high|few-active> " +
        "--warmup <count> --iterations <count> --output <path>");
    }

    private sealed class BenchmarkState
    {
      internal readonly int EntityCount;
      internal readonly string LoadProfile;
      internal readonly List<ISubscription<std_msgs.msg.Int64>> Subscriptions =
        new List<ISubscription<std_msgs.msg.Int64>>();
      internal readonly List<IPublisher<std_msgs.msg.Int64>> Publishers =
        new List<IPublisher<std_msgs.msg.Int64>>();
      internal readonly List<std_msgs.msg.Int64> Messages = new List<std_msgs.msg.Int64>();
      internal readonly List<int> ActiveEntityIndices = new List<int>();
      internal readonly List<string> ActiveTopics = new List<string>();
      internal readonly ConcurrentDictionary<long, long> PublishedAt =
        new ConcurrentDictionary<long, long>();
      internal readonly List<double> LatenciesMs = new List<double>();
      internal readonly object LatencyLock = new object();
      internal readonly List<double> SpinDurationMs;
      internal readonly object SpinDurationLock = new object();
      internal long Sequence;
      internal long PublishedMessages;
      internal long ReceivedMessages;
      internal long SpinCalls;
      internal long DrainSpinCalls;
      internal long SpinAllocatedBytes;

      internal void RecordSpinDuration(long stopwatchTicks)
      {
        double durationMs = ToMilliseconds(stopwatchTicks);
        lock (SpinDurationLock)
        {
          SpinDurationMs.Add(durationMs);
        }
      }

      internal bool HasActivePublishers
      {
        get { return Publishers.Count > 0; }
      }

      internal BenchmarkState(int entityCount, string loadProfile, int spinDurationCapacity)
      {
        EntityCount = entityCount;
        LoadProfile = loadProfile;
        SpinDurationMs = new List<double>(Math.Max(0, spinDurationCapacity));
      }

      internal bool IsActive(int index)
      {
        if (LoadProfile == "idle")
        {
          return false;
        }
        if (LoadProfile == "few-active")
        {
          return index < Math.Max(1, EntityCount / 100);
        }
        return true;
      }

      internal bool ShouldPublish(int iteration)
      {
        return LoadProfile == "high" ||
          LoadProfile == "few-active" ||
          (LoadProfile == "low" && iteration % 10 == 0);
      }

      internal void ResetMeasurement()
      {
        PublishedAt.Clear();
        lock (LatencyLock)
        {
          LatenciesMs.Clear();
        }
        lock (SpinDurationLock)
        {
          SpinDurationMs.Clear();
        }
        Interlocked.Exchange(ref PublishedMessages, 0);
        Interlocked.Exchange(ref ReceivedMessages, 0);
        Interlocked.Exchange(ref SpinCalls, 0);
        Interlocked.Exchange(ref DrainSpinCalls, 0);
        Interlocked.Exchange(ref SpinAllocatedBytes, 0);
      }
    }

    private sealed class BenchmarkRun
    {
      internal readonly BenchmarkMetrics Metrics;
      internal readonly long ProcessPrivateMemoryStartBytes;

      internal BenchmarkRun(BenchmarkMetrics metrics, long processPrivateMemoryStartBytes)
      {
        Metrics = metrics;
        ProcessPrivateMemoryStartBytes = processPrivateMemoryStartBytes;
      }
    }

    private sealed class BenchmarkResult
    {
      public int SchemaVersion { get; set; }
      public DateTimeOffset TimestampUtc { get; set; }
      public string Status { get; set; }
      public string StatusReason { get; set; }
      public string Commit { get; set; }
      public string SourceDirty { get; set; }
      public string SourceRoot { get; set; }
      public string Distro { get; set; }
      public string Rmw { get; set; }
      public string Os { get; set; }
      public string Cpu { get; set; }
      public string CpuArchitecture { get; set; }
      public int LogicalProcessorCount { get; set; }
      public string Mode { get; set; }
      public string EffectiveMode { get; set; }
      public int EntityCount { get; set; }
      public string LoadProfile { get; set; }
      public int WarmupIterations { get; set; }
      public int Iterations { get; set; }
      public string ShutdownScope { get; set; }
      public BenchmarkMetrics Metrics { get; set; }
    }

    private sealed class BenchmarkMetrics
    {
      public double AllocationBytesPerSpin { get; set; }
      public long WorkloadAllocatedBytes { get; set; }
      public long SpinCalls { get; set; }
      public long DrainSpinCalls { get; set; }
      public long SpinDurationSampleCount { get; set; }
      public double CpuSeconds { get; set; }
      public double CpuPercent { get; set; }
      public double ThroughputMessagesPerSecond { get; set; }
      public double? LatencyP50Ms { get; set; }
      public double? LatencyP95Ms { get; set; }
      public double? LatencyP99Ms { get; set; }
      public double? SpinDurationP50Ms { get; set; }
      public double? SpinDurationP95Ms { get; set; }
      public double? SpinDurationP99Ms { get; set; }
      public double CreateLatencyMs { get; set; }
      public double RemoveLatencyMs { get; set; }
      public double ShutdownLatencyMs { get; set; }
      public long? ProcessPrivateMemoryStartBytes { get; set; }
      public long? ProcessPrivateMemoryEndBytes { get; set; }
    }

    private sealed class BenchmarkArguments
    {
      internal string Distro;
      internal string Mode = "wait-set";
      internal int EntityCount = 1;
      internal string LoadProfile = "idle";
      internal int WarmupIterations = 100;
      internal int Iterations = 1000;
      internal string OutputPath = "ros2cs_spin_benchmark.json";
      internal bool ShowHelp;

      internal static BenchmarkArguments Parse(string[] args)
      {
        var result = new BenchmarkArguments
        {
          Distro = Environment.GetEnvironmentVariable("ROS_DISTRO") ?? "unknown"
        };

        for (int i = 0; i < args.Length; i++)
        {
          string value;
          switch (args[i])
          {
            case "--help":
            case "-h":
              result.ShowHelp = true;
              break;
            case "--distro":
              result.Distro = NextValue(args, ref i, "--distro");
              break;
            case "--mode":
              result.Mode = NextValue(args, ref i, "--mode").ToLowerInvariant();
              break;
            case "--entities":
              value = NextValue(args, ref i, "--entities");
              result.EntityCount = ParsePositiveInt(value, "--entities");
              break;
            case "--load":
              result.LoadProfile = NextValue(args, ref i, "--load").ToLowerInvariant();
              break;
            case "--warmup":
              value = NextValue(args, ref i, "--warmup");
              result.WarmupIterations = ParseNonNegativeInt(value, "--warmup");
              break;
            case "--iterations":
              value = NextValue(args, ref i, "--iterations");
              result.Iterations = ParsePositiveInt(value, "--iterations");
              break;
            case "--output":
              result.OutputPath = NextValue(args, ref i, "--output");
              break;
            default:
              throw new ArgumentException("Unknown argument: " + args[i]);
          }
        }

        if (result.ShowHelp)
        {
          return result;
        }
        if (String.IsNullOrWhiteSpace(result.Distro))
        {
          throw new ArgumentException("--distro must not be empty");
        }
        if (result.Mode != "wait-set" && result.Mode != "direct")
        {
          throw new ArgumentException("--mode must be wait-set or direct");
        }
        if (result.LoadProfile != "idle" && result.LoadProfile != "low" &&
            result.LoadProfile != "high" && result.LoadProfile != "few-active")
        {
          throw new ArgumentException("--load must be idle, low, high, or few-active");
        }
        return result;
      }

      private static string NextValue(string[] args, ref int index, string option)
      {
        if (index + 1 >= args.Length)
        {
          throw new ArgumentException(option + " requires a value");
        }
        return args[++index];
      }

      private static int ParsePositiveInt(string value, string option)
      {
        if (!Int32.TryParse(value, out int parsed) || parsed <= 0)
        {
          throw new ArgumentException(option + " must be a positive integer");
        }
        return parsed;
      }

      private static int ParseNonNegativeInt(string value, string option)
      {
        if (!Int32.TryParse(value, out int parsed) || parsed < 0)
        {
          throw new ArgumentException(option + " must be a non-negative integer");
        }
        return parsed;
      }
    }
  }
}
