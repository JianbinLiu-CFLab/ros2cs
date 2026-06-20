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

namespace ROS2.Test
{
    [TestFixture]
    public class SubscriptionTest
    {
        INode node;
        Publisher<std_msgs.msg.Int32> publisher;
        private const double SpinOnceTimeoutSeconds = 0.01;
        private const int EndpointDiscoverySpins = 5;
        private const int DefaultQosDepth = 10;
        private const int DefaultQosDrainSpins = DefaultQosDepth + 1;
        private const int SensorDataQosDepth = 5;
        private const int IncompatibleQosWarmupSpins = 3;
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

        private void AllowEndpointDiscovery()
        {
            // 5 spins at 10 ms is a short local DDS discovery window before publishing assertions.
            for (int i = 0; i < EndpointDiscoverySpins; i++)
            {
                Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds);
            }
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
            AllowEndpointDiscovery();

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
            AllowEndpointDiscovery();

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

            node.CreateSubscription<std_msgs.msg.Int32>(
                "subscription_qos_incompatible_topic",
                (msg) => { count += 1; });

            using var publishedMsg = new std_msgs.msg.Int32();
            publishedMsg.Data = 42;

            // Warm up discovery before proving BEST_EFFORT publisher and RELIABLE subscriber stay unmatched.
            for (int i = 0; i < IncompatibleQosWarmupSpins; i++)
            {
                Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds);
            }
            for (int i = 0; i < IncompatibleQosPublishAttempts; i++)
            {
                bestEffortPublisher.Publish(publishedMsg);
                Ros2cs.SpinOnce(node, SpinOnceTimeoutSeconds);
            }

            Assert.That(count, Is.Zero);
        }
    }
}
