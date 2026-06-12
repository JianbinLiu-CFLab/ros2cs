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
// - Added coverage for disposed node create-entity behavior.
// - Added node-owned entity disposal and stale-node pruning coverage.

using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using example_interfaces.srv;

namespace ROS2.Test
{
    [TestFixture]
    public class NodeTest
    {
        INode node;
        string TEST_NODE = "my_node";
        const string GRAPH_TEST_TOPIC = "/graph_discovery_test_topic";
        static readonly TimeSpan GraphTimeout = TimeSpan.FromSeconds(5);
        static readonly TimeSpan GraphPollInterval = TimeSpan.FromMilliseconds(100);

        [SetUp]
        public void SetUp()
        {
            Ros2cs.Init();
            node = Ros2cs.CreateNode(TEST_NODE);
        }

        [TearDown]
        public void TearDown()
        {
            if (!node.IsDisposed)
            {
                node.Dispose();
            }
            Ros2cs.Shutdown();
        }

        [Test]
        public void Accessors()
        {
            Assert.That(node.Name, Is.EqualTo("my_node"));
        }

        [Test]
        public void CreatePublisher()
        {
            Publisher<std_msgs.msg.Bool> publisher = node.CreatePublisher<std_msgs.msg.Bool>("test_topic");
            publisher.Dispose();

            using (publisher = node.CreatePublisher<std_msgs.msg.Bool>("test_topic"))
            {
            }
        }

        [Test]
        public void Publish()
        {
            using (Publisher<std_msgs.msg.Bool> publisher = node.CreatePublisher<std_msgs.msg.Bool>("test_topic"))
            {
                using var msg = new std_msgs.msg.Bool();
                publisher.Publish(msg);
            }
        }

        [Test]
        public void CreatePublisherAfterDisposeThrows()
        {
            node.Dispose();

            Assert.Throws<ObjectDisposedException>(() => node.CreatePublisher<std_msgs.msg.Bool>("test_topic"));
        }

        [Test]
        public void CountPublishersAndSubscribersTrackGraphEndpoints()
        {
            Assert.That(node.CountPublishers(GRAPH_TEST_TOPIC), Is.EqualTo(0));
            Assert.That(node.CountSubscribers(GRAPH_TEST_TOPIC), Is.EqualTo(0));

            using (node.CreatePublisher<std_msgs.msg.Bool>(GRAPH_TEST_TOPIC))
            {
                WaitForGraphCount(() => node.CountPublishers(GRAPH_TEST_TOPIC), Is.GreaterThanOrEqualTo(1));
            }
            WaitForGraphCount(() => node.CountPublishers(GRAPH_TEST_TOPIC), Is.EqualTo(0));

            using (node.CreateSubscription<std_msgs.msg.Bool>(GRAPH_TEST_TOPIC, msg => { }))
            {
                WaitForGraphCount(() => node.CountSubscribers(GRAPH_TEST_TOPIC), Is.GreaterThanOrEqualTo(1));
            }
            WaitForGraphCount(() => node.CountSubscribers(GRAPH_TEST_TOPIC), Is.EqualTo(0));
        }

        [Test]
        public void CountPublishersAndSubscribersAfterDisposeThrow()
        {
            node.Dispose();

            Assert.Throws<ObjectDisposedException>(() => node.CountPublishers(GRAPH_TEST_TOPIC));
            Assert.Throws<ObjectDisposedException>(() => node.CountSubscribers(GRAPH_TEST_TOPIC));
        }

        [Test]
        public void DirectDisposeAllowsSameNameNodeRecreate()
        {
            node.Dispose();

            node = Ros2cs.CreateNode(TEST_NODE);

            Assert.That(node.Name, Is.EqualTo(TEST_NODE));
        }

        [Test]
        public void PublishChangingSize()
        {
          using (Publisher<test_msgs.msg.UnboundedSequences> publisher = node.CreatePublisher<test_msgs.msg.UnboundedSequences>("test_topic"))
          {
            string[] setStringSequence = new string[2]
            {
              "First",
              "Second string to send, has to be a bit longer for the test"
            };

            using var msg3 = new test_msgs.msg.UnboundedSequences();
            msg3.String_values = setStringSequence;
            publisher.Publish(msg3);

            msg3.Int32_values = new int[2] { 1, 2 };
            msg3.String_values[0] = "A string that is longer than the previous one";
            msg3.String_values[1] = "shorter than previous one";

            // Publish reusing the message
            publisher.Publish(msg3);

            msg3.String_values = new string[5] { "1", "2", "3", "4", "5" };
            msg3.Int32_values = new int[10] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            publisher.Publish(msg3);

            msg3.String_values = new string[1] { "hello" };
            msg3.Int32_values = new int[1] { 1 };
            publisher.Publish(msg3);
          }
        }

        [Test]
        public void CreateSubscription()
        {
            Subscription<std_msgs.msg.Bool> subscription = node.CreateSubscription<std_msgs.msg.Bool>(
                "/subscription_topic", msg => Console.WriteLine("I heard: [" + msg.Data + "]"));
            subscription.Dispose();

            using (subscription = node.CreateSubscription<std_msgs.msg.Bool>(
                "test_topic", msg => Console.WriteLine("Got message")))
            {
            }
        }

        [Test]
        public void RemoveService()
        {
            var service = node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                "/test",
                request => { throw new InvalidOperationException("service should not be called"); }
            );
            
            Assert.That(node.RemoveService(service));
            Assert.That(service.IsDisposed);
        }

        [Test]
        public void RemoveClient()
        {
            var client = node.CreateClient<AddTwoInts_Request, AddTwoInts_Response>("/test");
            
            Assert.That(node.RemoveClient(client));
            Assert.That(client.IsDisposed);
        }

        [Test]
        public void NodeDisposeDisposesOwnedEntities()
        {
            var publisher = node.CreatePublisher<std_msgs.msg.Bool>("owned_publisher");
            var subscription = node.CreateSubscription<std_msgs.msg.Bool>("owned_subscription", msg => { });
            var service = node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                "owned_service",
                request => new AddTwoInts_Response());
            var client = node.CreateClient<AddTwoInts_Request, AddTwoInts_Response>("owned_service");

            node.Dispose();

            Assert.That(publisher.IsDisposed, Is.True);
            Assert.That(subscription.IsDisposed, Is.True);
            Assert.That(service.IsDisposed, Is.True);
            Assert.That(client.IsDisposed, Is.True);
        }

        [Test]
        public void PublisherHeldAfterNodeDisposeReturnsSafely()
        {
            var publisher = node.CreatePublisher<std_msgs.msg.Bool>("held_publisher");
            using var msg = new std_msgs.msg.Bool();

            node.Dispose();

            Assert.That(publisher.IsDisposed, Is.True);
            Assert.DoesNotThrow(() => publisher.Publish(msg));
        }

        [Test]
        public void NodeDisposedFlagIsVolatileForChildDisposeVisibility()
        {
            FieldInfo field = typeof(Node).GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            Assert.That(field.GetRequiredCustomModifiers(), Does.Contain(typeof(System.Runtime.CompilerServices.IsVolatile)));
        }

        [Test]
        public void ChildDisposedFlagsAreVolatileForEntityVisibility()
        {
            Type[] childTypes = {
                typeof(Publisher<std_msgs.msg.Bool>),
                typeof(Subscription<std_msgs.msg.Bool>),
                typeof(Service<AddTwoInts_Request, AddTwoInts_Response>),
                typeof(Client<AddTwoInts_Request, AddTwoInts_Response>)
            };

            foreach (Type childType in childTypes)
            {
                FieldInfo field = childType.GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, childType.FullName);
                Assert.That(
                    field.GetRequiredCustomModifiers(),
                    Does.Contain(typeof(System.Runtime.CompilerServices.IsVolatile)),
                    childType.FullName);
            }
        }

        private static void WaitForGraphCount(Func<int> readCount, IResolveConstraint expected)
        {
            DateTime deadline = DateTime.UtcNow + GraphTimeout;
            int count = readCount();
            while (!expected.Resolve().ApplyTo(count).IsSuccess)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.That(count, expected);
                }
                Thread.Sleep(GraphPollInterval);
                count = readCount();
            }
        }
    }
}
