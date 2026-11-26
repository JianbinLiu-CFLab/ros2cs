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
                Assert.That(
                    waitSet.GetNativeHandleAddress(WaitSetEntityKind.Subscription, 1),
                    Is.EqualTo(System.IntPtr.Zero));

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
        public void ResizeRetriesAfterNativePartialFailure()
        {
            Ros2cs.Init();
            INode node = null;
            NativeRcl.WaitSetResizeType originalResize = NativeRcl.rcl_wait_set_resize;
            try
            {
                node = Ros2cs.CreateNode("waitset_resize_retry_node");
                var subscription = node.CreateSubscription<std_msgs.msg.Int32>(
                    "waitset_resize_retry_topic",
                    _ => { });
                WaitSet waitSet = Ros2cs.GetWaitSetForTests();
                bool failOnce = true;
                NativeRcl.rcl_wait_set_resize = (
                    ref rcl_wait_set_t handle,
                    UIntPtr subscriptions,
                    UIntPtr guardConditions,
                    UIntPtr timers,
                    UIntPtr clients,
                    UIntPtr services,
                    UIntPtr events) =>
                {
                    int ret = originalResize(
                        ref handle,
                        subscriptions,
                        guardConditions,
                        timers,
                        clients,
                        services,
                        events);
                    if (failOnce)
                    {
                        failOnce = false;
                        return (int)RCLReturnEnum.RCL_RET_ERROR;
                    }
                    return ret;
                };

                Assert.Catch<Exception>(() => waitSet.Resize(1, 0, 0));
                NativeRcl.rcl_wait_set_resize = originalResize;

                Assert.DoesNotThrow(() => waitSet.Resize(1, 0, 0));
                Assert.That(
                    waitSet.TryAddSubscription(subscription, out ulong index),
                    Is.EqualTo(AddResult.SUCCESS));
                Assert.That(
                    waitSet.GetNativeHandleAddress(WaitSetEntityKind.Subscription, index),
                    Is.EqualTo(waitSet.GetManagedHandleAddress(
                        WaitSetEntityKind.Subscription,
                        0)));

                waitSet.Dispose();
                Assert.That(
                    waitSet.GetNativeHandleAddress(WaitSetEntityKind.Subscription, index),
                    Is.EqualTo(System.IntPtr.Zero));
            }
            finally
            {
                NativeRcl.rcl_wait_set_resize = originalResize;
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
