// Copyright 2019 Dyno Robotics (by Samuel Lindgren samuel@dynorobotics.se)
// Copyright 2019-2021 Robotec.ai
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Audited init/shutdown regression tests after lifecycle hardening.
// - Added lifecycle stress coverage and clarified spin timeout semantics.
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
using NUnit.Framework;

namespace ROS2.Test
{
    [TestFixture]
    public class InitShutdownTest
    {
        [TearDown]
        public void TearDown()
        {
            if (Ros2cs.Ok())
            {
                Ros2cs.Shutdown();
            }
        }

        [Test]
        public void Init()
        {
            Ros2cs.Init();
            Assert.That(Ros2cs.Ok(), Is.True);

            Assert.DoesNotThrow(() => Ros2cs.Shutdown());
            Assert.That(Ros2cs.Ok(), Is.False);
        }

        [Test]
        public void InitShutdown()
        {
            Ros2cs.Init();
            Assert.That(Ros2cs.Ok(), Is.True);

            Ros2cs.Shutdown();
            Assert.That(Ros2cs.Ok(), Is.False);
        }

        [Test]
        public void InitShutdownSequence()
        {
            Ros2cs.Init();
            Assert.That(Ros2cs.Ok(), Is.True);
            Ros2cs.Shutdown();
            Assert.That(Ros2cs.Ok(), Is.False);

            Ros2cs.Init();
            Assert.That(Ros2cs.Ok(), Is.True);
            Ros2cs.Shutdown();
            Assert.That(Ros2cs.Ok(), Is.False);
        }

        [Test]
        public void InitShutdownStress()
        {
            for (int i = 0; i < 50; ++i)
            {
                Ros2cs.Init();
                Assert.That(Ros2cs.Ok(), Is.True);
                Ros2cs.Shutdown();
                Assert.That(Ros2cs.Ok(), Is.False);
            }

            Assert.That(Ros2cs.Ok(), Is.False);
        }

        [Test]
        public void DoubleInit()
        {
            Ros2cs.Init();
            Ros2cs.Init();
            Assert.That(Ros2cs.Ok(), Is.True);

            Ros2cs.Shutdown();
            Assert.That(Ros2cs.Ok(), Is.False);
        }

        [Test]
        public void DoubleShutdown()
        {
            Ros2cs.Init();
            Assert.That(Ros2cs.Ok(), Is.True);

            Ros2cs.Shutdown();
            Assert.That(Ros2cs.Ok(), Is.False);
            Ros2cs.Shutdown();
            Assert.That(Ros2cs.Ok(), Is.False);
        }

        [Test]
        public void CreateNodeWithoutInit()
        {
            Assert.That(() => { Ros2cs.CreateNode("foo"); }, Throws.TypeOf<NotInitializedException>());
        }

        [Test]
        public void SpinEmptyNode()
        {
            Ros2cs.Init();
            try
            {
                var node = Ros2cs.CreateNode("TestNode");
                Assert.That(Ros2cs.SpinOnce(node), Is.False);
                var subscription = node.CreateSubscription<std_msgs.msg.Int32>(
                    "subscription_test_topic",
                    (msg) => { throw new InvalidOperationException("subscription callback was triggered"); }
                );
                // True means the non-empty wait set was populated and waited on; no message is expected here.
                Assert.That(Ros2cs.SpinOnce(node), Is.True);
            }
            finally
            {
                Ros2cs.Shutdown();
            }
        }
    }
}
