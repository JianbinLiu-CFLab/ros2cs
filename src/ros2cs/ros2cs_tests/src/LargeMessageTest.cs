// Copyright 2019 Dyno Robotics (by Samuel Lindgren samuel@dynorobotics.se)
// Copyright 2019-2021 Robotec.ai
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Audited large-message regression tests after generated type handling updates.
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

using NUnit.Framework;

namespace ROS2.Test
{
    [TestFixture]
    public class LargeMessageTest
    {
        private const int LargeSequenceLength = 1024;
        private const double FirstSentinelValue = 3.14;
        private const double LastSentinelValue = -42.5;

        INode subscriptionNode;
        INode publisherNode;
        Publisher<std_msgs.msg.Float64MultiArray> publisher;

        [SetUp]
        public void SetUp()
        {
            Ros2cs.Init();

            subscriptionNode = Ros2cs.CreateNode("subscription_test_node");
            publisherNode = Ros2cs.CreateNode("publisher_test_node");

            publisher = publisherNode.CreatePublisher<std_msgs.msg.Float64MultiArray>("subscription_test_topic");
        }

        [TearDown]
        public void TearDown()
        {
            publisher?.Dispose();
            publisherNode?.Dispose();
            subscriptionNode?.Dispose();
            Ros2cs.Shutdown();
        }

        [Test]
        public void SubscriptionTriggerCallback()
        {
            bool callbackTriggered = false;
            double[] receivedData = null;
            subscriptionNode.CreateSubscription<std_msgs.msg.Float64MultiArray>(
                "subscription_test_topic", (msg) =>
                {
                    callbackTriggered = true;
                    receivedData = msg.Data;
                });

            using var largeMsg = new std_msgs.msg.Float64MultiArray();
            // Guard variable-length sequence marshaling across the DDS boundary and verify both ends survive.
            largeMsg.Data = new double[LargeSequenceLength];
            largeMsg.Data[0] = FirstSentinelValue;
            largeMsg.Data[LargeSequenceLength - 1] = LastSentinelValue;

            publisher.Publish(largeMsg);
            Ros2cs.SpinOnce(subscriptionNode, 0.1);

            Assert.That(callbackTriggered, Is.True);
            Assert.That(receivedData, Is.Not.Null);
            Assert.That(receivedData.Length, Is.EqualTo(LargeSequenceLength));
            Assert.That(receivedData[0], Is.EqualTo(FirstSentinelValue));
            Assert.That(receivedData[LargeSequenceLength - 1], Is.EqualTo(LastSentinelValue));
        }
    }
}
