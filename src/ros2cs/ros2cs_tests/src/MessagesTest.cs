// Copyright 2019 Dyno Robotics (by Samuel Lindgren samuel@dynorobotics.se)
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
// - Added native roundtrip coverage for int8 sequences.
// - Added generated message finalizer policy coverage.
// - Expanded finalizer policy coverage across loaded generated message types.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace ROS2.Test
{
    [TestFixture]
    public class MessagesTest
    {
        [Test]
        public void CreateMessage()
        {
            using var msg = new std_msgs.msg.Bool();
            Assert.That(msg.IsDisposed, Is.False);
        }

        [Test]
        public void SetBoolData()
        {
            using var msg = new std_msgs.msg.Bool();
            Assert.That(msg.Data, Is.False);
            msg.Data = true;
            Assert.That(msg.Data, Is.True);
            msg.Data = false;
            Assert.That(msg.Data, Is.False);
        }

        [Test]
        public void SetInt64Data()
        {
            using var msg = new std_msgs.msg.Int64();
            Assert.That(msg.Data, Is.EqualTo(0));
            msg.Data = 12345;
            Assert.That(msg.Data, Is.EqualTo(12345));
        }

        [Test]
        public void SetStringData()
        {
            using var msg = new std_msgs.msg.String();
            Assert.That(msg.Data, Is.EqualTo(""));
            msg.Data = "Show me what you got!";
            Assert.That(msg.Data, Is.EqualTo("Show me what you got!"));
        }

        /// <summary>Generated messages must not call native destroy functions from finalizers.</summary>
        [Test]
        public void GeneratedMessagesDoNotDeclareFinalizer()
        {
            LoadGeneratedMessageAssemblies();

            var messageTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        return e.Types.Where(type => type != null);
                    }
                })
                .Where(type => type != null)
                .Where(type => typeof(Message).IsAssignableFrom(type))
                .Where(type => !type.IsAbstract)
                .ToArray();

            Assert.That(messageTypes, Is.Not.Empty);

            foreach (Type messageType in messageTypes)
            {
                var finalizeMethod = messageType.GetMethod(
                    "Finalize",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                Assert.That(finalizeMethod, Is.Null, messageType.FullName + " declares a finalizer.");
            }
        }

        private static void LoadGeneratedMessageAssemblies()
        {
            string outputDirectory = AppContext.BaseDirectory;
            string[] assemblyPaths = Directory.GetFiles(outputDirectory, "*_assembly.dll");
            Assert.That(assemblyPaths, Is.Not.Empty);

            foreach (string assemblyPath in assemblyPaths)
            {
                Assembly.LoadFrom(assemblyPath);
            }
        }

        [Test]
        public void SetDefaults()
        {
            using var msg = new test_msgs.msg.Defaults();
            msg.Int32_value = 24;
            Assert.That(msg.Int32_value, Is.EqualTo(24));
            msg.Float32_value = 3.14F;
            Assert.That(msg.Float32_value, Is.EqualTo(3.14F));
        }

        [Test]
        public void SetStrings()
        {
            using var msg = new test_msgs.msg.Strings();
            msg.String_value = "Turtles all the way down";
            Assert.That(msg.String_value, Is.EqualTo("Turtles all the way down"));
        }

        [Test]
        public void SetUnboundedSequences()
        {
            using var msg = new test_msgs.msg.UnboundedSequences();
            bool[] setBoolSequence = new bool[2];
            setBoolSequence[0] = true;
            setBoolSequence[1] = false;
            msg.Bool_values = setBoolSequence;

            bool[] getBoolSequence = msg.Bool_values;
            Assert.That(getBoolSequence.Length, Is.EqualTo(2));
            Assert.That(getBoolSequence[0], Is.True);
            Assert.That(getBoolSequence[1], Is.False);

            int[] setIntSequence = new int[2];
            setIntSequence[0] = 123;
            setIntSequence[1] = 456;
            using var msg2 = new test_msgs.msg.UnboundedSequences();
            msg2.Int32_values = setIntSequence;
            int[] getIntList = msg2.Int32_values;
            Assert.That(getIntList.Length, Is.EqualTo(2));
            Assert.That(getIntList[0], Is.EqualTo(123));
            Assert.That(getIntList[1], Is.EqualTo(456));

            string[] setStringSequence = new string[2];
            setStringSequence[0] = "Hello";
            setStringSequence[1] = "world";
            using var msg3 = new test_msgs.msg.UnboundedSequences();
            msg3.String_values = setStringSequence;
            string[] getStringSequence = msg3.String_values;
            Assert.That(getStringSequence.Length, Is.EqualTo(2));
            Assert.That(getStringSequence[0], Is.EqualTo("Hello"));
            Assert.That(getStringSequence[1], Is.EqualTo("world"));
        }

        /// <summary>Verifies generated int8 sequence marshaling preserves signed values through native storage.</summary>
        [Test]
        public void NativeRoundtripPreservesInt8Sequence()
        {
            sbyte[] expected = new sbyte[] { -128, -1, 0, 1, 127 };
            using var msg = new test_msgs.msg.UnboundedSequences();
            msg.Int8_values = expected;

            msg.WriteNativeMessage();
            msg.Int8_values = new sbyte[0];
            msg.ReadNativeMessage();

            Assert.That(msg.Int8_values, Is.EqualTo(expected));
        }

        /// <summary>Verifies generated primitive sequence marshaling preserves representative native layouts.</summary>
        [Test]
        public void NativeRoundtripPreservesRepresentativeSequences()
        {
            bool[] expectedBool = new bool[] { true, false, true };
            double[] expectedFloat64 = new double[] { -1.5, 0.0, 42.25 };
            string[] expectedString = new string[] { "hello", "", "世界" };
            using var msg = new test_msgs.msg.UnboundedSequences();
            msg.Bool_values = expectedBool;
            msg.Float64_values = expectedFloat64;
            msg.String_values = expectedString;

            msg.WriteNativeMessage();
            msg.Bool_values = Array.Empty<bool>();
            msg.Float64_values = Array.Empty<double>();
            msg.String_values = Array.Empty<string>();
            msg.ReadNativeMessage();

            Assert.That(msg.Bool_values, Is.EqualTo(expectedBool));
            Assert.That(msg.Float64_values, Is.EqualTo(expectedFloat64));
            Assert.That(msg.String_values, Is.EqualTo(expectedString));
        }

        /// <summary>Verifies generated fixed-size array marshaling preserves primitive, string, and nested values.</summary>
        [Test]
        public void NativeRoundtripPreservesFixedSizeArrays()
        {
            bool[] expectedBool = new bool[] { true, false, true };
            int[] expectedInt32 = new int[] { -7, 0, 42 };
            string[] expectedString = new string[] { "fixed", "", "数组" };
            int[] expectedNestedInt32 = new int[] { 11, 22, 33 };
            byte[] expectedConstants = new byte[] { 1, 2, 3 };
            int[] expectedDefaultsInt32 = new int[] { 101, 202, 303 };

            using var msg = new test_msgs.msg.Arrays();
            for (int i = 0; i < msg.Bool_values.Length; i++)
            {
                msg.Bool_values[i] = expectedBool[i];
                msg.Int32_values[i] = expectedInt32[i];
                msg.String_values[i] = expectedString[i];
                msg.String_values_default[i] = "default-" + i;
                msg.Basic_types_values[i] = new test_msgs.msg.BasicTypes
                {
                    Int32_value = expectedNestedInt32[i]
                };
                msg.Constants_values[i] = new test_msgs.msg.Constants
                {
                    Structure_needs_at_least_one_member = expectedConstants[i]
                };
                msg.Defaults_values[i] = new test_msgs.msg.Defaults
                {
                    Int32_value = expectedDefaultsInt32[i]
                };
            }

            msg.WriteNativeMessage();
            Array.Clear(msg.Bool_values, 0, msg.Bool_values.Length);
            Array.Clear(msg.Int32_values, 0, msg.Int32_values.Length);
            for (int i = 0; i < msg.String_values.Length; i++)
            {
                msg.String_values[i] = "";
                msg.Basic_types_values[i].Int32_value = -1;
                msg.Constants_values[i].Structure_needs_at_least_one_member = 0;
                msg.Defaults_values[i].Int32_value = -1;
            }

            msg.ReadNativeMessage();

            Assert.That(msg.Bool_values, Is.EqualTo(expectedBool));
            Assert.That(msg.Int32_values, Is.EqualTo(expectedInt32));
            Assert.That(msg.String_values, Is.EqualTo(expectedString));
            for (int i = 0; i < expectedNestedInt32.Length; i++)
            {
                Assert.That(msg.Basic_types_values[i].Int32_value, Is.EqualTo(expectedNestedInt32[i]));
                Assert.That(
                    msg.Constants_values[i].Structure_needs_at_least_one_member,
                    Is.EqualTo(expectedConstants[i]));
                Assert.That(msg.Defaults_values[i].Int32_value, Is.EqualTo(expectedDefaultsInt32[i]));
            }
        }

        /// <summary>Fresh generated messages must not allocate native storage just to read from it.</summary>
        [Test]
        public void ReadNativeMessageWithoutNativeHandleThrows()
        {
            using var msg = new std_msgs.msg.Empty();

            Assert.Throws<InvalidOperationException>(() => msg.ReadNativeMessage());
        }

        /// <summary>Handle-taking native read/write overloads must respect Dispose state before using a handle.</summary>
        [Test]
        public void NativeMessageOverloadsThrowAfterDispose()
        {
            var msg = new std_msgs.msg.Empty();

            msg.Dispose();

            Assert.Throws<ObjectDisposedException>(() => msg.ReadNativeMessage(IntPtr.Zero));
            Assert.Throws<ObjectDisposedException>(() => msg.WriteNativeMessage(IntPtr.Zero));
        }

        /// <summary>Generated type support access must respect Dispose state like handle access does.</summary>
        [Test]
        public void TypeSupportHandleThrowsAfterDispose()
        {
            var msg = new std_msgs.msg.Empty();
            ROS2.Internal.MessageInternals internals = msg;

            msg.Dispose();

            Assert.Throws<ObjectDisposedException>(() => { _ = internals.TypeSupportHandle; });
        }

        /// <summary>Generated parent messages own direct nested message members created by their constructor.</summary>
        [Test]
        public void DisposeReleasesDirectNestedMessages()
        {
            var msg = new test_msgs.msg.Nested();
            test_msgs.msg.BasicTypes nested = msg.Basic_types_value;
            _ = nested.Handle;

            msg.Dispose();

            Assert.That(msg.IsDisposed, Is.True);
            Assert.That(nested.IsDisposed, Is.True);
        }

        /// <summary>Direct nested members remain parent-owned after reading from native storage.</summary>
        [Test]
        public void DisposeReleasesDirectNestedMessagesAfterNativeRead()
        {
            var msg = new test_msgs.msg.Nested();
            test_msgs.msg.BasicTypes nested = msg.Basic_types_value;
            msg.Basic_types_value.Int32_value = 42;

            msg.WriteNativeMessage();
            msg.ReadNativeMessage();
            msg.Dispose();

            Assert.That(nested.IsDisposed, Is.True);
        }

        /// <summary>Nested sequence elements are caller-owned and must not be disposed by the parent message.</summary>
        [Test]
        public void DisposeDoesNotReleaseNestedSequenceElements()
        {
            using var nested = new test_msgs.msg.BasicTypes();
            var msg = new test_msgs.msg.UnboundedSequences
            {
                Basic_types_values = new[] { nested }
            };

            msg.Dispose();

            Assert.That(nested.IsDisposed, Is.False);
        }

        /// <summary>Sequence elements materialized by ReadNativeMessage are parent-owned, unlike caller-supplied elements.</summary>
        [Test]
        public void DisposeReleasesReadOwnedNestedSequenceElements()
        {
            using var callerOwnedNested = new test_msgs.msg.BasicTypes();
            var msg = new test_msgs.msg.UnboundedSequences
            {
                Basic_types_values = new[] { callerOwnedNested }
            };
            callerOwnedNested.Int32_value = 42;

            msg.WriteNativeMessage();
            msg.Basic_types_values = Array.Empty<test_msgs.msg.BasicTypes>();
            msg.ReadNativeMessage();
            test_msgs.msg.BasicTypes readOwnedNested = msg.Basic_types_values[0];

            msg.Dispose();

            Assert.That(callerOwnedNested.IsDisposed, Is.False);
            Assert.That(readOwnedNested.IsDisposed, Is.True);
        }

        /// <summary>Verifies generated wstring fields preserve Unicode through native storage.</summary>
        [Test]
        public void NativeRoundtripPreservesWStrings()
        {
            using var msg = new test_msgs.msg.WStrings();
            msg.Wstring_value = "Hello 世界";
            msg.Wstring_value_default1 = "Bonjour";
            msg.Wstring_value_default2 = "Hellö wörld!";
            msg.Wstring_value_default3 = "ハローワールド";

            msg.WriteNativeMessage();
            msg.Wstring_value = "";
            msg.Wstring_value_default1 = "";
            msg.Wstring_value_default2 = "";
            msg.Wstring_value_default3 = "";
            msg.ReadNativeMessage();

            Assert.That(msg.Wstring_value, Is.EqualTo("Hello 世界"));
            Assert.That(msg.Wstring_value_default1, Is.EqualTo("Bonjour"));
            Assert.That(msg.Wstring_value_default2, Is.EqualTo("Hellö wörld!"));
            Assert.That(msg.Wstring_value_default3, Is.EqualTo("ハローワールド"));
        }

        [Test]
        public void SetBoundedSequences()
        {
            using var msg = new test_msgs.msg.BoundedSequences();
            bool[] setBoolSequence = new bool[2];
            setBoolSequence[0] = true;
            setBoolSequence[1] = false;
            msg.Bool_values = setBoolSequence;

            bool[] getBoolSequence = msg.Bool_values;
            Assert.That(getBoolSequence.Length, Is.EqualTo(2));
            Assert.That(getBoolSequence[0], Is.True);
            Assert.That(getBoolSequence[1], Is.False);

            int[] setIntSequence = new int[2];
            setIntSequence[0] = 123;
            setIntSequence[1] = 456;
            using var msg2 = new test_msgs.msg.BoundedSequences();
            msg2.Int32_values = setIntSequence;
            int[] getIntList = msg2.Int32_values;
            Assert.That(getIntList.Length, Is.EqualTo(2));
            Assert.That(getIntList[0], Is.EqualTo(123));
            Assert.That(getIntList[1], Is.EqualTo(456));

            string[] setStringSequence = new string[2];
            setStringSequence[0] = "Hello";
            setStringSequence[1] = "world";
            using var msg3 = new test_msgs.msg.BoundedSequences();
            msg3.String_values = setStringSequence;
            string[] getStringSequence = msg3.String_values;
            Assert.That(getStringSequence.Length, Is.EqualTo(2));
            Assert.That(getStringSequence[0], Is.EqualTo("Hello"));
            Assert.That(getStringSequence[1], Is.EqualTo("world"));
        }

        [Test]
        public void SetNested()
        {
            using var msg = new test_msgs.msg.Nested();
            test_msgs.msg.BasicTypes basic_types_msg = msg.Basic_types_value;
            Assert.That(basic_types_msg.Int32_value, Is.EqualTo(0));
            basic_types_msg.Int32_value = 25;
            Assert.That(basic_types_msg.Int32_value, Is.EqualTo(25));
            test_msgs.msg.BasicTypes basic_types_msg2 = msg.Basic_types_value;
            Assert.That(basic_types_msg2.Int32_value, Is.EqualTo(25));
        }

        [Test]
        public void SetMultiNested()
        {
            using var msg = new test_msgs.msg.MultiNested();

            msg.Unbounded_sequence_of_unbounded_sequences = new test_msgs.msg.UnboundedSequences[3];
            using var setUnboundedSequences = new test_msgs.msg.UnboundedSequences();
            string[] string_array = new string[2];
            setUnboundedSequences.String_values = string_array;
            setUnboundedSequences.String_values[0] = "hello";

            msg.Unbounded_sequence_of_unbounded_sequences[0] = setUnboundedSequences;
            msg.Unbounded_sequence_of_unbounded_sequences[0].String_values[1] = "world";

            Assert.That(msg.Unbounded_sequence_of_unbounded_sequences.Length, Is.EqualTo(3));

            var getUnboundedOfUnbounded = msg.Unbounded_sequence_of_unbounded_sequences;

            Assert.That(getUnboundedOfUnbounded[0].String_values[0], Is.EqualTo("hello"));
            Assert.That(getUnboundedOfUnbounded[0].String_values[1], Is.EqualTo("world"));
        }
    }
}
