// Copyright 2019-2021 Robotec.ai
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Added coverage for service callback exceptions during direct service take.
// - Added explicit service QoS request/response coverage.
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
    [TestFixture]
    public class ServiceTest
    {
        private static readonly string SERVICE_NAME = "test_service";

        private INode Node;

        private IService<AddTwoInts_Request, AddTwoInts_Response> Service;

        private Func<AddTwoInts_Request, AddTwoInts_Response> OnRequest =
            msg => throw new InvalidOperationException("callback not set");

        private AddTwoInts_Request CreateRequest(int a, int b)
        {
            var msg = new AddTwoInts_Request();
            msg.A = a;
            msg.B = b;
            return msg;
        }

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
            Service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(SERVICE_NAME, OnRequest);
        }

        [TearDown]
        public void TearDown()
        {
            if (!Node.IsDisposed)
            {
                Node.Dispose();
            }
        }

        [Test]
        public void DisposedServiceHandling()
        {
            Assert.That(!Service.IsDisposed);
            Service.Dispose();
            Assert.That(Service.IsDisposed);
            Assert.DoesNotThrow(() => { Ros2cs.SpinOnce(Node, 0.1); });
        }

        [Test]
        public void ReinitializeDisposedService()
        {
            Service.Dispose();
            Service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(SERVICE_NAME, OnRequest);
            Assert.DoesNotThrow(() => { Ros2cs.SpinOnce(Node, 0.1); });
        }

        [Test]
        public void ServiceAcceptsExplicitServicesQos()
        {
            Service.Dispose();
            using var qos = new QualityOfServiceProfile(QosPresetProfile.SERVICES_DEFAULT);
            Service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                SERVICE_NAME,
                msg =>
                {
                    var response = new AddTwoInts_Response();
                    response.Sum = msg.A + msg.B;
                    return response;
                },
                qos);
            using var client = Node.CreateClient<AddTwoInts_Request, AddTwoInts_Response>(SERVICE_NAME, qos);

            Task<AddTwoInts_Response> pendingResponse = client.CallAsync(CreateRequest(4, 5));
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!pendingResponse.IsCompleted && DateTime.UtcNow < deadline)
            {
                Ros2cs.SpinOnce(Node, 0.1);
            }

            Assert.That(pendingResponse.IsCompletedSuccessfully, Is.True);
            Assert.That(pendingResponse.Result.Sum, Is.EqualTo(9));
        }

        [Test]
        public void CallbackExceptionDoesNotEscapeTakeMessage()
        {
            bool callbackCalled = false;
            Service.Dispose();
            Service = Node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                SERVICE_NAME,
                msg =>
                {
                    callbackCalled = true;
                    throw new InvalidOperationException("expected test callback failure");
                });

            using var client = Node.CreateClient<AddTwoInts_Request, AddTwoInts_Response>(SERVICE_NAME);
            Task<AddTwoInts_Response> pendingResponse = client.CallAsync(CreateRequest(1, 2));

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!callbackCalled && DateTime.UtcNow < deadline)
            {
                Assert.DoesNotThrow(() => Service.TakeMessage());
                Thread.Sleep(10);
            }

            Assert.That(callbackCalled, Is.True);
            Assert.That(pendingResponse.IsCompleted, Is.False);
            Assert.That(client.Cancel(pendingResponse), Is.True);
        }
    }
}
