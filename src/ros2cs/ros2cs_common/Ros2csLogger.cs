// Copyright 2021 Robotec.ai
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
// - Made singleton initialization and logger callbacks thread-safe.
// - Serialized console formatting to reduce interleaved log noise.
// - Added PascalCase callback registration while retaining the legacy method.
// - Isolated application logger callback exceptions from ros2cs callers.

using System;
using System.Collections.Generic;

namespace ROS2
{
  public enum LogLevel
  {
    DEBUG,
    INFO,
    WARNING,
    ERROR
  }

  /// <summary> A simple logging class for Ros2cs </summary>
  public class Ros2csLogger
  {
    private Ros2csLogger() { }
    // Lazy<T> gives thread-safe singleton creation without a manual double-check lock.
    private static readonly Lazy<Ros2csLogger> Instance = new Lazy<Ros2csLogger>(() => new Ros2csLogger());
    // Protects mutable log level, callbacks, and console color writes.
    private static readonly object LoggerMutex = new object();
    private static LogLevel _logLevel;

    public delegate void Callback(object message);

    private static Dictionary<LogLevel, String> LevelNames = new Dictionary<LogLevel, String>()
    {
      {LogLevel.DEBUG, "DEBUG"},
      {LogLevel.INFO, "INFO"},
      {LogLevel.WARNING, "WARNING"},
      {LogLevel.ERROR, "ERROR"},
    };

    /// <summary>Minimum level that will be emitted by the logger.</summary>
    public static LogLevel LogLevel
    {
      get
      {
        lock (LoggerMutex)
        {
          return _logLevel;
        }
      }
      set
      {
        lock (LoggerMutex)
        {
          _logLevel = value;
        }
      }
    }

    private static Dictionary<LogLevel, Callback> LevelCallbacks = new Dictionary<LogLevel, Callback>()
    {
      {LogLevel.DEBUG, null},
      {LogLevel.INFO, null},
      {LogLevel.WARNING, null},
      {LogLevel.ERROR, null},
    };

    private static Dictionary<LogLevel, ConsoleColor> LevelColors = new Dictionary<LogLevel, ConsoleColor>()
    {
      {LogLevel.DEBUG, ConsoleColor.Green},
      {LogLevel.INFO, ConsoleColor.White},
      {LogLevel.WARNING, ConsoleColor.Yellow},
      {LogLevel.ERROR, ConsoleColor.Red},
    };

    /// <summary> Set a callback for an application layer logger </summary>
    /// <description> Can be useful to standardize logging between Ros2cs and
    /// an application (e. g. in Unity3D) which is using it </description>
    /// <param name="level"> Log level as in LogLevel enum </param>
    /// <param name="cb"> Callback (logging mechanism) to execute when logging </param>
    public static void SetCallback(LogLevel level, Callback cb)
    {
      lock (LoggerMutex)
      {
        LevelCallbacks[level] = cb;
      }
    }

    /// <summary>Legacy callback registration name retained for compatibility.</summary>
    [Obsolete("Use SetCallback.")]
    public static void setCallback(LogLevel level, Callback cb)
    {
      SetCallback(level, cb);
    }

    /// <summary> Acquire the singleton </summary>
    /// <description> Implements lazy construction </description>
    public static Ros2csLogger GetInstance()
    {
      return Instance.Value;
    }

    /// <summary> Log a given message with a set level </summary>
    /// <param name="level"> Log level as in LogLevel enum </param>
    /// <param name="message"> Message to log </param>
    public void Log(LogLevel level, String message)
    {
      Callback callback;
      lock (LoggerMutex)
      {
        if (_logLevel > level) return;
        callback = Ros2csLogger.LevelCallbacks[level];
      }

      // Threshold and callback are a snapshot: later LogLevel changes do not cancel a message already accepted.
      // Invoke application callbacks outside the console lock so custom loggers cannot block formatting.
      try
      {
        callback?.Invoke("[ROS2CS] " + message);
      }
      catch (Exception e)
      {
        Console.Error.WriteLine("[ROS2CS] Logger callback failed: " + e);
      }

      lock (LoggerMutex)
      {
        ConsoleColor prevForeground = Console.ForegroundColor;
        try
        {
          Console.ForegroundColor = Ros2csLogger.LevelColors[level];
          Console.WriteLine(
            "[" +
            DateTime.Now.ToString("HH:mm:ss.ffffff") +
            "][" +
            Ros2csLogger.LevelNames[level] +
            "] " +
            message);
        }
        finally
        {
          Console.ForegroundColor = prevForeground;
        }
      }
    }

    public void LogInfo(String message)
    {
      Log(LogLevel.INFO, message);
    }

    public void LogWarning(String message)
    {
      Log(LogLevel.WARNING, message);
    }

    public void LogError(String message)
    {
      Log(LogLevel.ERROR, message);
    }

    public void LogDebug(String message)
    {
      Log(LogLevel.DEBUG, message);
    }
  }
}
