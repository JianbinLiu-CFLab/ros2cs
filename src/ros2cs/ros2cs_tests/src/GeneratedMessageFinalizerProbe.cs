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
// - Added a child-process probe for generated message finalizer safety.

using System;
using System.Runtime.CompilerServices;

namespace ROS2.Test
{
    /// <summary>
    /// Standalone process used by CTest to prove leaked generated messages do not crash during finalizer processing.
    /// </summary>
    internal static class GeneratedMessageFinalizerProbe
    {
        /// <summary>Create a generated message, drop it without Dispose, force finalizers, and return the process result.</summary>
        public static int Main(string[] args)
        {
            try
            {
                CreateLeakedGeneratedString();
                ForceFinalizerSweep();
                Console.WriteLine("GENERATED_MESSAGE_FINALIZER_PROBE_PASS");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return 1;
            }
        }

        /// <summary>Create a generated string message with an allocated native handle, then intentionally leak ownership.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CreateLeakedGeneratedString()
        {
            std_msgs.msg.String msg = new std_msgs.msg.String();
            msg.Data = "generated_message_finalizer_probe";
            msg.WriteNativeMessage();
        }

        /// <summary>Force finalizers in this child process so native crashes do not kill the NUnit runner.</summary>
        private static void ForceFinalizerSweep()
        {
            for (int i = 0; i < 3; ++i)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }
}
