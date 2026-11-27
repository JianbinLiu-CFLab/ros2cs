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
// - Added coverage that spin processing continues after one subscription callback throws.
// - Added QoS reliability compatibility coverage.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ROS2.Test
{
    [TestFixture]
    public class SubscriptionTest
    {
        INode node;
        Publisher<std_msgs.msg.Int32> publisher;
        private const double SpinOnceTimeoutSeconds = 0.01;
        private static readonly TimeSpan EndpointDiscoveryTimeout = TimeSpan.FromSeconds(5);
        private const int DefaultQosDepth = 10;
        private const int DefaultQosDrainSpins = DefaultQosDepth + 1;
        private const int SensorDataQosDepth = 5;
        private const int IncompatibleQosPublishAttempts = 3;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Ros2cs.Init();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (Ros2cs.Ok())
            {
                Ros2cs.Shutdown();
            }
        }

        [SetUp]
        public void SetUp()
        {
            node = Ros2cs.CreateNode("subscription_test_node");
            publisher = node.CreatePublisher<std_msgs.msg.Int32>("subscription_test_topic");
        }

        [TearDown]
        public void TearDown()
        {
            publisher.Dispose();
            node.Dispose();
        }

        private void AssertPublisherMatch(Publisher<std_msgs.msg.Int32> candidate)
        {
            Assert.That(
                SpinWait.SpinUntil(
                    () => candidate.GetSubscriptionCountForTests() > 0,
                    EndpointDiscoveryTimeout),
                Is.True,
                "DDS publisher did not match a subscriber in time");
        }

        [Test]
        public void SubscriptionTriggerCallback()
        {
            bool callbackTriggered = false;
            node.CreateSubscription<std_msgs.msg.Int32>("subscription_test_topic", (msg) => { callbackTriggered = true; });
            using var publishedMsg = new std_msgs.msg.Int32();
            publisher.Publish(publishedMsg);
            Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds);

            Assert.That(callbackTriggered, Is.True);
        }

        [Test]
        public void RepeatedSpinOnceReusesSnapshotStorage()
        {
            node.CreateSubscription<std_msgs.msg.Int32>(
                "snapshot_reuse_allocation_topic",
                _ => { });

            for (int i = 0; i < 8; i++)
            {
                Ros2cs.SpinOnce(node, 0.0);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            const int measuredSpins = 32;
            long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < measuredSpins; i++)
            {
                Ros2cs.SpinOnce(node, 0.0);
            }

            double allocatedPerSpin =
                (double)(GC.GetAllocatedBytesForCurrentThread() - allocatedStart) / measuredSpins;
            Assert.That(allocatedPerSpin, Is.LessThan(128.0));
        }

        [Test]
        public void NestedSpinOnAnotherNodePreservesOuterSnapshot()
        {
            using var outerNode = Ros2cs.CreateNode("nested_snapshot_outer_node");
            using var innerNode = Ros2cs.CreateNode("nested_snapshot_inner_node");
            using var outerPublisher =
                outerNode.CreatePublisher<std_msgs.msg.Int32>("nested_snapshot_outer_topic");
            using var innerPublisher =
                innerNode.CreatePublisher<std_msgs.msg.Int32>("nested_snapshot_inner_topic");

            int outerCallbackCount = 0;
            int innerCallbackCount = 0;
            bool nestedSpinInvoked = false;
            outerNode.CreateSubscription<std_msgs.msg.Int32>(
                "nested_snapshot_outer_topic",
                _ =>
                {
                    outerCallbackCount++;
                    if (!nestedSpinInvoked)
                    {
                        nestedSpinInvoked = true;
                        Ros2cs.SpinOnce(innerNode, 0.1);
                    }
                });
            innerNode.CreateSubscription<std_msgs.msg.Int32>(
                "nested_snapshot_inner_topic",
                _ => innerCallbackCount++);

            Assert.That(
                SpinWait.SpinUntil(
                    () => outerPublisher.GetSubscriptionCountForTests() > 0 &&
                          innerPublisher.GetSubscriptionCountForTests() > 0,
                    EndpointDiscoveryTimeout),
                Is.True,
                "DDS publishers did not match nested snapshot subscriptions");

            using var outerMessage = new std_msgs.msg.Int32();
            using var innerMessage = new std_msgs.msg.Int32();
            outerPublisher.Publish(outerMessage);
            innerPublisher.Publish(innerMessage);

            for (int i = 0; i < 10 && outerCallbackCount == 0; i++)
            {
                Ros2cs.SpinOnce(outerNode, SpinOnceTimeoutSeconds);
            }

            Assert.That(outerCallbackCount, Is.EqualTo(1));
            Assert.That(innerCallbackCount, Is.EqualTo(1));
        }

        [Test]
        public void ConcurrentSpinOnSeparateNodesUsesIndependentSnapshots()
        {
            using var firstNode = Ros2cs.CreateNode("concurrent_snapshot_first_node");
            using var secondNode = Ros2cs.CreateNode("concurrent_snapshot_second_node");
            using var firstPublisher =
                firstNode.CreatePublisher<std_msgs.msg.Int32>("concurrent_snapshot_first_topic");
            using var secondPublisher =
                secondNode.CreatePublisher<std_msgs.msg.Int32>("concurrent_snapshot_second_topic");

            int firstCallbackCount = 0;
            int secondCallbackCount = 0;
            firstNode.CreateSubscription<std_msgs.msg.Int32>(
                "concurrent_snapshot_first_topic",
                _ => Interlocked.Increment(ref firstCallbackCount));
            secondNode.CreateSubscription<std_msgs.msg.Int32>(
                "concurrent_snapshot_second_topic",
                _ => Interlocked.Increment(ref secondCallbackCount));

            Assert.That(
                SpinWait.SpinUntil(
                    () => firstPublisher.GetSubscriptionCountForTests() > 0 &&
                          secondPublisher.GetSubscriptionCountForTests() > 0,
                    EndpointDiscoveryTimeout),
                Is.True,
                "DDS publishers did not match concurrent snapshot subscriptions");

            using var firstMessage = new std_msgs.msg.Int32();
            using var secondMessage = new std_msgs.msg.Int32();
            firstPublisher.Publish(firstMessage);
            secondPublisher.Publish(secondMessage);

            using var startGate = new ManualResetEventSlim(false);
            Task firstSpin = Task.Run(() =>
            {
                startGate.Wait();
                Ros2cs.SpinOnce(firstNode, 0.1);
            });
            Task secondSpin = Task.Run(() =>
            {
                startGate.Wait();
                Ros2cs.SpinOnce(secondNode, 0.1);
            });
            startGate.Set();

            Assert.That(Task.WaitAll(new[] { firstSpin, secondSpin }, TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(firstCallbackCount, Is.EqualTo(1));
            Assert.That(secondCallbackCount, Is.EqualTo(1));
        }

        [Test]
        public void SubscriptionCallbackMessageData()
        {
            int messageData = 12345;
            node.CreateSubscription<std_msgs.msg.Int32>("subscription_test_topic", (msg) => { messageData = msg.Data; });
            using var published_msg = new std_msgs.msg.Int32();
            published_msg.Data = 42;
            publisher.Publish(published_msg);
            Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds);

            Assert.That(messageData, Is.EqualTo(42));
        }

        [Test]
        public void SpinOnceContinuesAfterSubscriptionCallbackException()
        {
            bool secondCallbackTriggered = false;
            node.CreateSubscription<std_msgs.msg.Int32>(
                "subscription_test_topic",
                msg => { throw new InvalidOperationException("expected test callback failure"); });
            node.CreateSubscription<std_msgs.msg.Int32>(
                "subscription_test_topic",
                msg => { secondCallbackTriggered = true; });

            using var publishedMsg = new std_msgs.msg.Int32();
            publishedMsg.Data = 42;
            publisher.Publish(publishedMsg);

            for (int i = 0; i < 10 && !secondCallbackTriggered; i++)
            {
                Assert.DoesNotThrow(() => { Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds); });
            }

            Assert.That(secondCallbackTriggered, Is.True);
        }

        [Test]
        public void DisposedSubscriptionHandling()
        {
            ISubscription<std_msgs.msg.Int32> subscriber =
              node.CreateSubscription<std_msgs.msg.Int32>("subscription_test_topic", (msg) => { });
            subscriber.Dispose();
            Assert.DoesNotThrow( () => { Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds); });
        }

        [Test]
        public void MultipleDisposedSubscriptionsHandling()
        {
            int numberOfSubscribers = 10;
            List<Subscription<std_msgs.msg.Int32>> subscriptions = new List<Subscription<std_msgs.msg.Int32>>();
            for(int i = 0; i < numberOfSubscribers; i++)
            {
                subscriptions.Add(
                    node.CreateSubscription<std_msgs.msg.Int32>("subscription_test_topic", (msg) => { }));
            }
            Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds);
            subscriptions.ForEach(delegate(Subscription<std_msgs.msg.Int32> subscription)
            {
                subscription.Dispose();
            });
            Assert.DoesNotThrow( () => { Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds); });
        }

        [Test]
        public void ReinitializeDisposedSubscriber()
        {
            ISubscription<std_msgs.msg.Int32> subscriber =
              node.CreateSubscription<std_msgs.msg.Int32>("subscription_test_topic", (msg) => { });
            subscriber.Dispose();
            subscriber =
              node.CreateSubscription<std_msgs.msg.Int32>("subscription_test_topic", (msg) => { });
            Assert.DoesNotThrow( () => { Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds); });
        }

        [Test]
        public void SubscriptionQosDefaultDepth()
        {
            int count = 0;
            node.CreateSubscription<std_msgs.msg.Int32>("subscription_test_topic",
                                                        (msg) => { count += 1; });
            AssertPublisherMatch(publisher);

            using var published_msg = new std_msgs.msg.Int32();
            published_msg.Data = 42;

            // Exercise the default depth of 10, then spin once extra to drain callbacks fully.
            for (int i = 0; i < DefaultQosDepth; i++)
            {
                publisher.Publish(published_msg);
            }

            for (int i = 0; i < DefaultQosDrainSpins; i++)
            {
                Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds);
            }

            Assert.That(count, Is.EqualTo(10));
        }

        [Test]
        public void SubscriptionQosSensorDataDepth()
        {
            int count = 0;
            using var qosProfile = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);

            // A RELIABLE publisher can satisfy a BEST_EFFORT subscriber; the subscriber still uses
            // the SENSOR_DATA depth of 5.
            node.CreateSubscription<std_msgs.msg.Int32>("subscription_test_topic",
                                                        (msg) => { count += 1; },
                                                        qosProfile);
            AssertPublisherMatch(publisher);

            using var published_msg = new std_msgs.msg.Int32();
            published_msg.Data = 42;

            // Publish one more sample than SENSOR_DATA depth to prove the queue bound is enforced.
            for (int i = 0; i < SensorDataQosDepth + 1; i++)
            {
                publisher.Publish(published_msg);
            }

            for (int i = 0; i < DefaultQosDrainSpins; i++)
            {
                Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds);
            }

            // DDS scheduling can coalesce or drop samples before the test drains the queue; the
            // stable contract is that SENSOR_DATA depth bounds delivery to at most 5 queued samples.
            Assert.That(count, Is.InRange(1, 5));
        }

        [Test]
        public void BestEffortPublisherDoesNotMatchReliableSubscriber()
        {
            int count = 0;
            using var qosProfile = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);
            using var bestEffortPublisher =
                node.CreatePublisher<std_msgs.msg.Int32>("subscription_qos_incompatible_topic", qosProfile);
            using var compatiblePublisher =
                node.CreatePublisher<std_msgs.msg.Int32>("subscription_qos_incompatible_topic");

            node.CreateSubscription<std_msgs.msg.Int32>(
                "subscription_qos_incompatible_topic",
                (msg) => { count += 1; });

            using var publishedMsg = new std_msgs.msg.Int32();
            publishedMsg.Data = 42;

            AssertPublisherMatch(compatiblePublisher);
            for (int i = 0; i < IncompatibleQosPublishAttempts; i++)
            {
                bestEffortPublisher.Publish(publishedMsg);
                Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds);
            }

            Assert.That(bestEffortPublisher.GetSubscriptionCountForTests(), Is.Zero);
            Assert.That(count, Is.Zero);
        }
    }
}
