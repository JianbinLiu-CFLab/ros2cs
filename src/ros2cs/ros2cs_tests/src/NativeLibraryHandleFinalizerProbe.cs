// Copyright 2019-2021 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Modifications by Jianbin Liu:
// - Added a child-process probe for NativeLibraryHandle finalizer safety.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ROS2.Test
{
    /// <summary>
    /// Standalone process that verifies a leaked NativeLibraryHandle finalizer does not invoke a loader unload.
    /// </summary>
    /// <remarks>
    /// This intentionally uses a fake loader. It verifies finalizer ownership semantics in an isolated process,
    /// but does not attempt to emulate Unity's partially unloaded Mono/native host.
    /// </remarks>
    internal static class NativeLibraryHandleFinalizerProbe
    {
        /// <summary>Forces finalization of an unreleased handle and returns a nonzero exit code on any unload.</summary>
        public static int Main(string[] args)
        {
            try
            {
                var loader = new RecordingDllLoadUtils();
                WeakReference handleReference = CreateUnreleasedHandle(loader);

                for (int attempt = 0; handleReference.IsAlive && attempt < 3; attempt++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                if (handleReference.IsAlive)
                {
                    Console.Error.WriteLine("NATIVE_LIBRARY_HANDLE_FINALIZER_PROBE_FAILED: handle remained alive.");
                    return 2;
                }

                if (loader.FreeLibraryCalls != 0)
                {
                    Console.Error.WriteLine(
                        "NATIVE_LIBRARY_HANDLE_FINALIZER_PROBE_FAILED: FreeLibrary calls=" + loader.FreeLibraryCalls);
                    return 3;
                }

                GC.KeepAlive(loader);
                Console.WriteLine("NATIVE_LIBRARY_HANDLE_FINALIZER_PROBE_PASS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("NATIVE_LIBRARY_HANDLE_FINALIZER_PROBE_EXCEPTION: " + exception);
                return 1;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateUnreleasedHandle(RecordingDllLoadUtils loader)
        {
            return new WeakReference(NativeLibraryHandle.FromHandle(loader, new IntPtr(1)));
        }

        /// <summary>Thread-safe fake loader used to observe unload calls from the finalizer thread.</summary>
        private sealed class RecordingDllLoadUtils : DllLoadUtils
        {
            private int freeLibraryCalls;

            public int FreeLibraryCalls
            {
                get { return Volatile.Read(ref freeLibraryCalls); }
            }

            public IntPtr LoadLibrary(string fileName) { return new IntPtr(1); }

            public IntPtr LoadLibraryNoSuffix(string fileName) { return new IntPtr(1); }

            public void FreeLibrary(IntPtr handle)
            {
                Interlocked.Increment(ref freeLibraryCalls);
            }

            public IntPtr GetProcAddress(IntPtr dllHandle, string name) { return IntPtr.Zero; }
        }
    }
}
