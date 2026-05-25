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

using System;
using ROS2;

namespace Examples
{
  internal sealed class ROS2ExampleRuntime : IDisposable
  {
    private readonly object mutex = new object();
    private bool disposed;

    public ROS2ExampleRuntime()
    {
      Ros2cs.Init();
      Console.CancelKeyPress += OnCancelKeyPress;
      AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private void OnCancelKeyPress(object sender, ConsoleCancelEventArgs args)
    {
      args.Cancel = true;
      Dispose();
    }

    private void OnProcessExit(object sender, EventArgs args)
    {
      Dispose();
    }

    public void Dispose()
    {
      lock (mutex)
      {
        if (disposed)
        {
          return;
        }

        disposed = true;
        Console.CancelKeyPress -= OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        if (Ros2cs.Ok())
        {
          Ros2cs.Shutdown();
        }
      }
    }
  }
}
