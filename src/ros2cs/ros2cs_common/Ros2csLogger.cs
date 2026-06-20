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
// - Serialized console writes to reduce interleaved log noise.
// - Added PascalCase callback registration while retaining the legacy method.
// - Isolated application logger callback exceptions from ros2cs callers.
// - Made console logging tolerant of headless runtimes without a valid console handle.

using System;
using System.IO;

namespace ROS2
{
  /// <summary>Severity threshold used by <see cref="Ros2csLogger"/>.</summary>
  public enum LogLevel
  {
    /// <summary>Diagnostic messages intended for development and detailed troubleshooting.</summary>
    DEBUG,
    /// <summary>Informational runtime messages.</summary>
    INFO,
    /// <summary>Recoverable or noteworthy runtime warnings.</summary>
    WARNING,
    /// <summary>Errors that indicate an operation failed or a callback threw.</summary>
    ERROR
  }

  /// <summary> A simple logging class for Ros2cs </summary>
  public class Ros2csLogger
  {
    private Ros2csLogger() { }
    // Lazy<T> gives thread-safe singleton creation without a manual double-check lock.
    private static readonly Lazy<Ros2csLogger> Instance = new Lazy<Ros2csLogger>(() => new Ros2csLogger());
    // Protects mutable callbacks and console writes.
    private static readonly object LoggerMutex = new object();
    private static volatile LogLevel _logLevel;

    /// <summary>Application-provided logging sink invoked with the formatted ros2cs message.</summary>
    public delegate void Callback(object message);

    /// <summary>Application-provided handler for exceptions thrown by logging callbacks.</summary>
    public delegate void CallbackExceptionHandler(LogLevel level, object message, Exception exception);

    private static readonly string[] LevelNames = new string[]
    {
      "DEBUG",
      "INFO",
      "WARNING",
      "ERROR",
    };

    /// <summary>Minimum level that will be emitted by the logger.</summary>
    public static LogLevel LogLevel
    {
      get
      {
        return _logLevel;
      }
      set
      {
        _logLevel = value;
      }
    }

    private static readonly Callback[] LevelCallbacks = new Callback[]
    {
      null,
      null,
      null,
      null,
    };

    private static CallbackExceptionHandler callbackExceptionHandler;

    /// <summary> Set a callback for an application layer logger </summary>
    /// <description> Can be useful to standardize logging between Ros2cs and
    /// an application (e. g. in Unity3D) which is using it </description>
    /// <param name="level"> Log level as in LogLevel enum </param>
    /// <param name="cb"> Callback (logging mechanism) to execute when logging </param>
    public static void SetCallback(LogLevel level, Callback cb)
    {
      lock (LoggerMutex)
      {
        LevelCallbacks[(int)level] = cb;
      }
    }

    /// <summary>Register an optional handler for exceptions thrown by application logger callbacks.</summary>
    /// <description>Useful in headless runtimes where stderr may not be visible.</description>
    /// <param name="handler">Handler invoked when a registered log callback throws, or null to clear.</param>
    public static void SetCallbackExceptionHandler(CallbackExceptionHandler handler)
    {
      lock (LoggerMutex)
      {
        callbackExceptionHandler = handler;
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

    /// <summary>
    /// Write to stderr, swallowing console errors from headless processes, Unity domain reloads,
    /// or runtimes where the console handle has already been closed.
    /// </summary>
    private static void TryWriteConsoleError(string message)
    {
      try
      {
        Console.Error.WriteLine(message);
      }
      catch (IOException)
      {
      }
      catch (ObjectDisposedException)
      {
      }
      catch (InvalidOperationException)
      {
      }
    }

    private static string FormatConsoleLine(LogLevel level, string message)
    {
      return string.Concat(
        "[",
        DateTime.Now.ToString("HH:mm:ss.ffffff"),
        "][",
        Ros2csLogger.LevelNames[(int)level],
        "] ",
        message);
    }

    /// <summary>
    /// Write to stdout, swallowing console errors from headless processes, Unity domain reloads,
    /// or runtimes where the console handle has already been closed.
    /// </summary>
    private static void TryWriteConsoleLine(string line)
    {
      try
      {
        Console.WriteLine(line);
      }
      catch (IOException)
      {
      }
      catch (ObjectDisposedException)
      {
      }
      catch (InvalidOperationException)
      {
      }
    }

    /// <summary> Log a given message with a set level </summary>
    /// <param name="level"> Log level as in LogLevel enum </param>
    /// <param name="message"> Message to log </param>
    public void Log(LogLevel level, String message)
    {
      Callback callback;
      CallbackExceptionHandler exceptionHandler;
      lock (LoggerMutex)
      {
        if (_logLevel > level) return;
        callback = Ros2csLogger.LevelCallbacks[(int)level];
        exceptionHandler = callbackExceptionHandler;
      }

      // Threshold and callback are a snapshot: later LogLevel changes do not cancel a message already accepted.
      // Invoke application callbacks outside the console lock so custom loggers cannot block formatting.
      if (callback != null)
      {
        string callbackMessage = "[ROS2CS] " + message;
        try
        {
          callback.Invoke(callbackMessage);
        }
        catch (Exception e)
        {
          if (exceptionHandler != null)
          {
            try
            {
              exceptionHandler.Invoke(level, callbackMessage, e);
            }
            catch (Exception handlerException)
            {
              TryWriteConsoleError("[ROS2CS] Logger callback exception handler failed: " + handlerException);
            }
          }
          else
          {
            TryWriteConsoleError("[ROS2CS] Logger callback failed: " + e);
          }
        }
      }

      string line = FormatConsoleLine(level, message);
      lock (LoggerMutex)
      {
        TryWriteConsoleLine(line);
      }
    }

    /// <summary>Log an informational message.</summary>
    public void LogInfo(String message)
    {
      Log(LogLevel.INFO, message);
    }

    /// <summary>Log a warning message.</summary>
    public void LogWarning(String message)
    {
      Log(LogLevel.WARNING, message);
    }

    /// <summary>Log an error message.</summary>
    public void LogError(String message)
    {
      Log(LogLevel.ERROR, message);
    }

    /// <summary>Log a DEBUG message that has already been constructed.</summary>
    public void LogDebug(String message)
    {
      Log(LogLevel.DEBUG, message);
    }

    /// <summary>
    /// Log a DEBUG message using a deferred factory.
    /// </summary>
    /// <remarks>
    /// The factory is invoked only when DEBUG logging is enabled, avoiding string allocation
    /// and expensive formatting on hotter paths.
    /// </remarks>
    public void LogDebug(Func<string> messageFactory)
    {
      // This fast path intentionally reads the volatile level outside LoggerMutex to avoid
      // constructing debug messages when DEBUG logging is disabled.
      if (_logLevel > LogLevel.DEBUG)
      {
        return;
      }
      if (messageFactory == null)
      {
        throw new ArgumentNullException(nameof(messageFactory));
      }
      Log(LogLevel.DEBUG, messageFactory());
    }
  }
}
