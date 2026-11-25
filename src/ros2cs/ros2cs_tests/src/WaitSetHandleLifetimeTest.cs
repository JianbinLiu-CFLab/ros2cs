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

using NUnit.Framework;
using example_interfaces.srv;

namespace ROS2.Test
{
    [TestFixture]
    public class WaitSetHandleLifetimeTest
    {
        [Test]
        public void RegisteredSubscriptionUsesStableStorageAddress()
        {
            Ros2cs.Init();
            INode node = null;
            try
            {
                node = Ros2cs.CreateNode("waitset_handle_lifetime_node");
                var subscription = node.CreateSubscription<std_msgs.msg.Int32>(
                    "waitset_handle_lifetime_topic",
                    _ => { });
                WaitSet waitSet = Ros2cs.GetWaitSetForTests();

                waitSet.Resize(1, 0, 0);
                Assert.That(
                    waitSet.TryAddSubscription(subscription, out ulong index),
                    Is.EqualTo(AddResult.SUCCESS));

                Assert.That(
                    waitSet.GetManagedHandleAddress(WaitSetEntityKind.Subscription, 0),
                    Is.Not.EqualTo(System.IntPtr.Zero));
                Assert.That(
                    waitSet.GetNativeHandleAddress(WaitSetEntityKind.Subscription, index),
                    Is.EqualTo(waitSet.GetManagedHandleAddress(
                        WaitSetEntityKind.Subscription,
                        0)));

                waitSet.Resize(1, 0, 0);
                Assert.That(
                    waitSet.GetNativeHandleAddress(WaitSetEntityKind.Subscription, index),
                    Is.EqualTo(System.IntPtr.Zero));
            }
            finally
            {
                node?.Dispose();
                Ros2cs.Shutdown();
            }
        }

        [Test]
        public void RegisteredClientAndServiceUseStableStorageAddresses()
        {
            Ros2cs.Init();
            INode node = null;
            try
            {
                node = Ros2cs.CreateNode("waitset_client_service_lifetime_node");
                var client = node.CreateClient<AddTwoInts_Request, AddTwoInts_Response>(
                    "waitset_client_service_lifetime_topic");
                var service = node.CreateService<AddTwoInts_Request, AddTwoInts_Response>(
                    "waitset_client_service_lifetime_topic",
                    _ => new AddTwoInts_Response());
                WaitSet waitSet = Ros2cs.GetWaitSetForTests();

                waitSet.Resize(0, 1, 1);
                Assert.That(
                    waitSet.TryAddClient(client, out ulong clientIndex),
                    Is.EqualTo(AddResult.SUCCESS));
                Assert.That(
                    waitSet.TryAddService(service, out ulong serviceIndex),
                    Is.EqualTo(AddResult.SUCCESS));

                Assert.That(
                    waitSet.GetNativeHandleAddress(WaitSetEntityKind.Client, clientIndex),
                    Is.EqualTo(waitSet.GetManagedHandleAddress(WaitSetEntityKind.Client, 0)));
                Assert.That(
                    waitSet.GetNativeHandleAddress(WaitSetEntityKind.Service, serviceIndex),
                    Is.EqualTo(waitSet.GetManagedHandleAddress(WaitSetEntityKind.Service, 0)));

                waitSet.Resize(0, 1, 1);
                Assert.That(
                    waitSet.GetNativeHandleAddress(WaitSetEntityKind.Client, clientIndex),
                    Is.EqualTo(System.IntPtr.Zero));
                Assert.That(
                    waitSet.GetNativeHandleAddress(WaitSetEntityKind.Service, serviceIndex),
                    Is.EqualTo(System.IntPtr.Zero));
            }
            finally
            {
                node?.Dispose();
                Ros2cs.Shutdown();
            }
        }
    }
}
