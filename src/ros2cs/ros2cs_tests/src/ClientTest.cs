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
// - Added disposed client availability coverage.
// - Added synchronous call reentry guard coverage.
// - Added explicit service QoS client-call coverage.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using example_interfaces.srv;

namespace ROS2.Test
{
    [TestFixture]
    public class ClientTest
    {
        private static readonly string SERVICE_NAME = "test_service";
        private static readonly TimeSpan SpinTimeout = TimeSpan.FromSeconds(5);

        private INode Node;

        private IClient<AddTwoInts_Request, AddTwoInts_Response> Client;

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
            Node = Ros2cs.CreateNode("service_test_node");
            Client = Node.CreateClient<AddTwoInts_Request, AddTwoInts_Response>(SERVICE_NAME);
        }

        [TearDown]
        public void TearDown()
        {
            if (!Node.IsDisposed)
            {
                Node.Dispose();
            }
        }

        private AddTwoInts_Request CreateRequest(int a, int b)
        {
            var msg = new AddTwoInts_Request();
            msg.A = a;
            msg.B = b;
            return msg;
        }

        private AddTwoInts_Response HandleRequest(AddTwoInts_Request msg)
        {
            var response = new AddTwoInts_Response();
            response.Sum = msg.A + msg.B;
            return response;
        }

        private void SpinUntil(Func<bool> condition, string timeoutMessage)
        {
            DateTime deadline = DateTime.UtcNow + SpinTimeout;
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail(timeoutMessage);
                }
                Ros2cs.SpinOnce(Node, 0.1);
            }
        }

        [Test]
        public void ClientCall()
        {
            using var service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                SERVICE_NAME,
                HandleRequest
            );

            bool spinDone = false;
            Task spinTask = Task.Run(() =>
            {
                while (!Volatile.Read(ref spinDone))
                {
                    Ros2cs.SpinOnce(Node, 0.1);
                }
            });

            try
            {
                using AddTwoInts_Response response = Client.Call(CreateRequest(8, 13), SpinTimeout);
                Assert.That(response.Sum, Is.EqualTo(21));
            }
            finally
            {
                Volatile.Write(ref spinDone, true);
                Assert.That(spinTask.Wait(TimeSpan.FromSeconds(1)), Is.True);
            }
        }

        [Test]
        public void ClientCallFromSpinCallbackThrows()
        {
            using var service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                SERVICE_NAME,
                HandleRequest
            );
            using var publisher = Node.CreatePublisher<std_msgs.msg.Int32>("client_reentry_topic");
            InvalidOperationException callException = null;
            bool callbackTriggered = false;
            Node.CreateSubscription<std_msgs.msg.Int32>(
                "client_reentry_topic",
                msg =>
                {
                    callbackTriggered = true;
                    callException = Assert.Throws<InvalidOperationException>(
                        () => Client.Call(CreateRequest(1, 2), TimeSpan.FromMilliseconds(100)));
                });

            using var publishedMsg = new std_msgs.msg.Int32();
            publishedMsg.Data = 1;
            publisher.Publish(publishedMsg);
            SpinUntil(() => callbackTriggered, "Timed out waiting for reentry test callback.");

            Assert.That(callException, Is.Not.Null);
            Assert.That(callException.Message, Does.Contain("Synchronous Client.Call cannot be used from a spin callback"));
        }

        [Test]
        public void ClientCallAsync()
        {
            using var service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                SERVICE_NAME,
                HandleRequest
            );
            var task = Client.CallAsync(CreateRequest(42, 3));
            SpinUntil(() => task.IsCompleted, "Timed out waiting for service response.");
            Assert.That(task.Result.Sum, Is.EqualTo(45));
        }

        [Test]
        public void ClientAndServiceAcceptExplicitServicesQos()
        {
            Client.Dispose();
            using var qos = new QualityOfServiceProfile(QosPresetProfile.SERVICES_DEFAULT);
            using var service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                SERVICE_NAME,
                HandleRequest,
                qos
            );
            Client = Node.CreateClient<AddTwoInts_Request, AddTwoInts_Response>(SERVICE_NAME, qos);

            var task = Client.CallAsync(CreateRequest(6, 7));
            SpinUntil(() => task.IsCompleted, "Timed out waiting for explicit QoS service response.");

            Assert.That(task.Result.Sum, Is.EqualTo(13));
        }

        [Test]
        public void ClientCallAsyncConcurrent()
        {
            using var service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                SERVICE_NAME,
                HandleRequest
            );
            Task<AddTwoInts_Response>[] tasks = Enumerable
                .Range(0, 10)
                .Select(i => Client.CallAsync(CreateRequest(i, 100 - i)))
                .ToArray();
            SpinUntil(() => tasks.All(task => task.IsCompleted), "Timed out waiting for concurrent service responses.");
            Assert.That(tasks.Select(task => task.Result.Sum), Is.All.EqualTo(100));
        }

        [Test]
        public void ClientWaitForService()
        {
            Assert.That(Client.IsServiceAvailable(), Is.False);
            {
                using var service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                    SERVICE_NAME,
                    HandleRequest
                );
                SpinUntil(
                    () => Client.IsServiceAvailable(),
                    "Timed out waiting for service to become available.");
            }
            SpinUntil(
                () => !Client.IsServiceAvailable(),
                "Timed out waiting for service to become unavailable.");
        }

        [Test]
        public void ClientTryWaitForService()
        {
            Assert.That(Client.TryWaitForService(TimeSpan.FromMilliseconds(100)), Is.False);

            using var service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                SERVICE_NAME,
                HandleRequest
            );

            Assert.That(Client.TryWaitForService(SpinTimeout), Is.True);
        }

        [Test]
        public void ClientWaitForServiceAfterDisposeReturnsFalse()
        {
            Client.Dispose();

            Assert.That(Client.IsServiceAvailable(), Is.False);
        }

        [Test]
        public void DisposedClientHandling()
        {
            Assert.That(Client.IsDisposed, Is.False);
            Client.Dispose();
            Assert.That(Client.IsDisposed);
            Assert.DoesNotThrow(() => { Ros2cs.SpinOnce(Node, 0.1); });
        }

        [Test]
        public void DisposedClientTasks()
        {
            Ros2cs.SpinOnce(Node, 0.1);
            using var service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                SERVICE_NAME,
                HandleRequest
            );
            var task = Client.CallAsync(CreateRequest(42, 3));
            Client.Dispose();

            Assert.That(Client.IsDisposed);
            Assert.Throws<AggregateException>(task.Wait);
            Assert.That(task.IsFaulted);
            Assert.That(task.Exception.InnerExceptions.Any(e => e is ObjectDisposedException));
            Assert.DoesNotThrow(() => { Ros2cs.SpinOnce(Node, 0.1); });
        }

        [Test]
        public void ReinitializeDisposedClient()
        {
            Client.Dispose();
            Client = Node.CreateClient<AddTwoInts_Request, AddTwoInts_Response>(SERVICE_NAME);
            Assert.DoesNotThrow(() => { Ros2cs.SpinOnce(Node, 0.1); });
        }

        [Test]
        public void ClientPendingRequests()
        {
            using var service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                SERVICE_NAME,
                HandleRequest
            );
            Task[] tasks = Enumerable
                .Range(0, 3)
                .Select(i => this.CreateRequest(i, 100 - i))
                .Select(request => this.Client.CallAsync(request))
                .ToArray();

            Assert.That(this.Client.PendingRequests.Count, Is.EqualTo(3));

            SpinUntil(() => tasks.Any(task => task.IsCompleted), "Timed out waiting for any pending request to complete.");

            int completed = tasks.Where(task => task.IsCompletedSuccessfully).Count();

            Assert.That(completed, Is.GreaterThan(0));
            Assert.That(this.Client.PendingRequests.Count, Is.EqualTo(tasks.Length - completed));
        }

        [Test]
        public void ClientPendingRequestsCast()
        {
            IReadOnlyDictionary<long, Task> requests = (this.Client as IClientBase).PendingRequests;
            Task pendingTask = this.Client.CallAsync(this.CreateRequest(3, 4));

            Assert.That(requests.Keys, Is.EquivalentTo(this.Client.PendingRequests.Keys));
            Assert.That(this.Client.Cancel(pendingTask), Is.True);
        }

        [Test]
        public void ClientCancel()
        {
            Assert.That(this.Client.Cancel(Task.CompletedTask), Is.False);

            using var service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                SERVICE_NAME,
                HandleRequest
            );
            Task finishedTask = this.Client.CallAsync(this.CreateRequest(3, 4));

            SpinUntil(() => finishedTask.IsCompleted, "Timed out waiting for finished request before cancel check.");

            Assert.That(this.Client.Cancel(finishedTask), Is.False);

            Task pendingTask = this.Client.CallAsync(this.CreateRequest(3, 4));

            Assert.That(this.Client.Cancel(pendingTask));
            Assert.That(pendingTask.IsCanceled);
            Assert.That(this.Client.PendingRequests.Count, Is.Zero);

            Assert.DoesNotThrow(() => { Ros2cs.SpinOnce(Node, 0.1); });            
        }
    }
}
