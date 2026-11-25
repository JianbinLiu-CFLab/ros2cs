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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using example_interfaces.srv;

namespace ROS2.Test
{
    /// <summary>Regression tests for entity destruction while the wait set is active.</summary>
    [TestFixture]
    public class WaitSetLifecycleRaceTest
    {
        private const double WaitTimeoutSeconds = 0.05;
        private static readonly TimeSpan WaitActiveTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan SpinJoinTimeout = TimeSpan.FromSeconds(2);
        private string previousSpinFallback;

        [SetUp]
        public void SetUp()
        {
            previousSpinFallback = Environment.GetEnvironmentVariable("ROS2CS_SPIN_FALLBACK");
            Environment.SetEnvironmentVariable("ROS2CS_SPIN_FALLBACK", "off");
            Ros2cs.Init();
        }

        [TearDown]
        public void TearDown()
        {
            if (Ros2cs.Ok())
            {
                Ros2cs.Shutdown();
            }
            Environment.SetEnvironmentVariable("ROS2CS_SPIN_FALLBACK", previousSpinFallback);
        }

        [Test]
        public void SubscriptionDisposeWaitsForActiveWait()
        {
            var node = Ros2cs.CreateNode("waitset_subscription_dispose_race_node");
            var subscription = node.CreateSubscription<std_msgs.msg.Int32>(
                "waitset_subscription_dispose_race_topic",
                _ => { });

            for (int i = 0; i < 5; i++)
            {
                Task<Exception> spinTask = StartSpin(node);
                WaitForActiveWait();
                subscription.Dispose();
                AssertSpinCompleted(spinTask);

                if (i < 4)
                {
                    subscription = node.CreateSubscription<std_msgs.msg.Int32>(
                        "waitset_subscription_dispose_race_topic_" + i,
                        _ => { });
                }
            }
        }

        [Test]
        public void ClientDisposeWaitsForActiveWait()
        {
            var node = Ros2cs.CreateNode("waitset_client_dispose_race_node");
            var client = node.CreateClient<AddTwoInts_Request, AddTwoInts_Response>(
                "waitset_client_dispose_race_service");

            Task<Exception> spinTask = StartSpin(node);
            WaitForActiveWait();
            client.Dispose();
            AssertSpinCompleted(spinTask);
        }

        [Test]
        public void ServiceDisposeWaitsForActiveWait()
        {
            var node = Ros2cs.CreateNode("waitset_service_dispose_race_node");
            var service = node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                "waitset_service_dispose_race_service",
                _ => new AddTwoInts_Response());

            Task<Exception> spinTask = StartSpin(node);
            WaitForActiveWait();
            service.Dispose();
            AssertSpinCompleted(spinTask);
        }

        [Test]
        public void NodeDisposeWaitsForActiveWait()
        {
            var node = Ros2cs.CreateNode("waitset_node_dispose_race_node");
            node.CreateSubscription<std_msgs.msg.Int32>(
                "waitset_node_dispose_race_topic",
                _ => { });

            Task<Exception> spinTask = StartSpin(node);
            WaitForActiveWait();
            node.Dispose();
            AssertSpinCompleted(spinTask);
            Assert.That(node.IsDisposed, Is.True);
        }

        [Test]
        public void RemoveNodeWaitsForActiveWait()
        {
            var node = Ros2cs.CreateNode("waitset_remove_node_race_node");
            node.CreateSubscription<std_msgs.msg.Int32>(
                "waitset_remove_node_race_topic",
                _ => { });

            Task<Exception> spinTask = StartSpin(node);
            WaitForActiveWait();
            Assert.That(Ros2cs.RemoveNode(node), Is.True);
            AssertSpinCompleted(spinTask);
            Assert.That(node.IsDisposed, Is.True);
        }

        [Test]
        public void DisposeFromCallbackDoesNotDeadlock()
        {
            var node = Ros2cs.CreateNode("waitset_callback_dispose_node");
            Subscription<std_msgs.msg.Int32> subscription = null;
            using var callbackCompleted = new ManualResetEventSlim(false);
            var publisher = node.CreatePublisher<std_msgs.msg.Int32>(
                "waitset_callback_dispose_topic");
            subscription = node.CreateSubscription<std_msgs.msg.Int32>(
                "waitset_callback_dispose_topic",
                _ =>
                {
                    subscription.Dispose();
                    callbackCompleted.Set();
                });

            Task spinTask = Task.Run(() => Ros2cs.Spin(node, 0.01));
            WaitForActiveWait();
            using (var message = new std_msgs.msg.Int32())
            {
                message.Data = 1;
                publisher.Publish(message);
            }

            Assert.That(callbackCompleted.Wait(SpinJoinTimeout), Is.True);
            Ros2cs.Shutdown();
            Assert.That(spinTask.Wait(SpinJoinTimeout), Is.True);
            Assert.That(subscription.IsDisposed, Is.True);
        }

        [Test]
        public void FinalizerPathSkipsNativeFiniWhenWaitSetGateIsBusy()
        {
            var node = Ros2cs.CreateNode("waitset_finalizer_gate_node");
            var subscription = node.CreateSubscription<std_msgs.msg.Int32>(
                "waitset_finalizer_gate_topic",
                _ => { });

            Task finalizerPath;
            lock (Ros2cs.GetWaitSetMutexForTests())
            {
                finalizerPath = Task.Run(() =>
                    ((INodeChildEntity)subscription).DisposeFromNode(false));
                Assert.That(finalizerPath.Wait(SpinJoinTimeout), Is.True);
            }

            Assert.That(subscription.IsDisposed, Is.False);
            subscription.Dispose();
        }

        [Test]
        public void ConcurrentEntityCreateAndDisposeWhileSpinningDoesNotThrow()
        {
            var node = Ros2cs.CreateNode("waitset_concurrent_create_node");
            Task spinTask = Task.Run(() => Ros2cs.Spin(node, 0.01));
            Task[] creators =
            {
                Task.Run(() => CreateAndDisposeEntities(node, "a")),
                Task.Run(() => CreateAndDisposeEntities(node, "b"))
            };

            Assert.That(Task.WaitAll(creators, TimeSpan.FromSeconds(10)), Is.True);
            Ros2cs.Shutdown();
            Assert.That(spinTask.Wait(SpinJoinTimeout), Is.True);
        }

        private static Task<Exception> StartSpin(INode node)
        {
            return Task.Run<Exception>(() =>
            {
                try
                {
                    Ros2cs.SpinOnce(node, WaitTimeoutSeconds);
                    return null;
                }
                catch (Exception e)
                {
                    return e;
                }
            });
        }

        private static void CreateAndDisposeEntities(INode node, string suffix)
        {
            for (int i = 0; i < 5; i++)
            {
                string topic = "waitset_concurrent_" + suffix + "_" + i;
                var publisher = node.CreatePublisher<std_msgs.msg.Int32>(topic);
                var subscription = node.CreateSubscription<std_msgs.msg.Int32>(topic, _ => { });
                var client = node.CreateClient<AddTwoInts_Request, AddTwoInts_Response>(topic + "_service");
                var service = node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                    topic + "_service",
                    _ => new AddTwoInts_Response());

                service.Dispose();
                client.Dispose();
                subscription.Dispose();
                publisher.Dispose();
            }
        }

        private static void WaitForActiveWait()
        {
            Assert.That(
                SpinWait.SpinUntil(() => Ros2cs.IsWaitSetWaitActiveForTests, WaitActiveTimeout),
                Is.True,
                "SpinOnce did not enter the native wait-set wait");
        }

        private static void AssertSpinCompleted(Task<Exception> spinTask)
        {
            Assert.That(spinTask.Wait(SpinJoinTimeout), Is.True);
            Assert.That(spinTask.Result, Is.Null);
        }
    }
}
