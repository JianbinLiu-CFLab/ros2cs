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
// - Fixed native test helper option ownership so allocated options are released by teardown.
// - Added missing native subscription cleanup in the subscription fixture.
// - Fixed native publisher allocation parameter and wait-set cleanup in native tests.
// - Added fail-fast native return checks and guarded cleanup in QoS native tests.
// - Asserted native option-dispose return codes for wrappers that expose rcl fini.

using NUnit.Framework;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using ROS2.Test;
using ROS2.Internal;
using example_interfaces.srv;

namespace ROS2.TestNativeMethods
{
    [TestFixture]
    public class RCLInitialize
    {
        public static void InitRcl(ref rcl_context_t context)
        {
            NativeRcl.rcl_reset_error();
            rcl_init_options_t init_options = NativeRcl.rcl_get_zero_initialized_init_options();
            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
            var ret = (RCLReturnEnum)NativeRcl.rcl_init_options_init(ref init_options, allocator);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK));

            context = NativeRcl.rcl_get_zero_initialized_context();

            ret = (RCLReturnEnum)NativeRcl.rcl_init(0, null, ref init_options, ref context);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK), Utils.PopRclErrorString());
            ret = (RCLReturnEnum)NativeRcl.rcl_init_options_fini(ref init_options);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK), Utils.PopRclErrorString());
            Assert.That(NativeRcl.rcl_context_is_valid(ref context), Is.True);
        }

        public static void ShutdownRcl(ref rcl_context_t context)
        {
            var ret = (RCLReturnEnum)NativeRcl.rcl_shutdown(ref context);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK));

            ret = (RCLReturnEnum)NativeRcl.rcl_context_fini(ref context);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK));
        }

        [Test]
        public void InitShutdownFinalize()
        {
            rcl_context_t context = new rcl_context_t();
            InitRcl(ref context);
            ShutdownRcl(ref context);
        }

        [Test]
        public void NativeInterfaceInitShutdownFinalize()
        {
            rcl_context_t context = NativeRcl.rcl_get_zero_initialized_context();
            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();

            TestUtils.AssertRetOk(NativeRclInterface.rclcs_init(ref context, allocator));
            Assert.That(NativeRcl.rcl_context_is_valid(ref context), Is.True);
            ShutdownRcl(ref context);
        }
    }

    [TestFixture]
    public class RCL
    {
        [Test]
        public void GetZeroInitializedContext()
        {
            rcl_context_t context = NativeRcl.rcl_get_zero_initialized_context();
            Assert.That(context, Is.EqualTo(default(rcl_context_t)));
        }

        [Test]
        public void GetDefaultAllocator()
        {
            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
            Assert.That(allocator.allocate, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(allocator.deallocate, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(allocator.reallocate, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(allocator.zero_allocate, Is.Not.EqualTo(IntPtr.Zero));
        }

        [Test]
        public void GetZeroInitializedInitOptions()
        {
            rcl_init_options_t init_options = NativeRcl.rcl_get_zero_initialized_init_options();
            Assert.That(init_options, Is.EqualTo(default(rcl_init_options_t)));
        }

        [Test]
        public void RequestIdHeaderIsBlittableWithoutManagedGuidArray()
        {
            FieldInfo writerGuid = typeof(rcl_rmw_request_id_t).GetField("writer_guid");

            Assert.That(writerGuid, Is.Null);
            // 16-byte writer_guid plus 8-byte int64 sequence number in rmw_request_id_t.
            Assert.That(Marshal.SizeOf<rcl_rmw_request_id_t>(), Is.EqualTo(24));
        }

        [Test]
        public void NativeLayoutSizesMatchRclHeaders()
        {
            Assert.That(
                Marshal.SizeOf<rcl_node_t>(),
                Is.EqualTo(checked((int)NativeRclInterface.rclcs_sizeof_rcl_node_t().ToUInt64())));
            Assert.That(
                Marshal.SizeOf<rcl_context_t>(),
                Is.EqualTo(checked((int)NativeRclInterface.rclcs_sizeof_rcl_context_t().ToUInt64())));
            Assert.That(
                Marshal.SizeOf<rcl_wait_set_t>(),
                Is.EqualTo(checked((int)NativeRclInterface.rclcs_sizeof_rcl_wait_set_t().ToUInt64())));
            Assert.That(
                Marshal.SizeOf<rcl_rmw_request_id_t>(),
                Is.EqualTo(checked((int)NativeRclInterface.rclcs_sizeof_rcl_rmw_request_id_t().ToUInt64())));
        }

        [Test]
        public void InitOptionsInit()
        {
            rcl_init_options_t init_options = NativeRcl.rcl_get_zero_initialized_init_options();
            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
            int ret = NativeRcl.rcl_init_options_init(ref init_options, allocator);
            Assert.That((RCLReturnEnum)ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK));
            ret = NativeRcl.rcl_init_options_fini(ref init_options);
            Assert.That((RCLReturnEnum)ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK));
        }

        [Test]
        public void GetErrorString()
        {
            NativeRcl.rcl_reset_error();
            string message = Utils.GetRclErrorString();
            Assert.That(message, Is.EqualTo("error not set"));
        }

        [Test]
        public void ResetError()
        {
            NativeRcl.rcl_reset_error();
            Assert.That(Utils.GetRclErrorString(), Is.EqualTo("error not set"));
        }

        [Test]
        public void InitValidArgs()
        {
            rcl_init_options_t init_options = NativeRcl.rcl_get_zero_initialized_init_options();
            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
            int initRet = NativeRcl.rcl_init_options_init(ref init_options, allocator);
            Assert.That((RCLReturnEnum)initRet, Is.EqualTo(RCLReturnEnum.RCL_RET_OK));
            rcl_context_t context = NativeRcl.rcl_get_zero_initialized_context();

            var ret = (RCLReturnEnum)NativeRcl.rcl_init(
                2, new string[] { "foo", "bar" }, ref init_options, ref context);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK));

            Assert.That(NativeRcl.rcl_context_is_valid(ref context), Is.True);
            ret = (RCLReturnEnum)NativeRcl.rcl_shutdown(ref context);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK));
            ret = (RCLReturnEnum)NativeRcl.rcl_init_options_fini(ref init_options);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK), Utils.PopRclErrorString());

            ret = (RCLReturnEnum)NativeRcl.rcl_context_fini(ref context);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK));
        }
    }

    [TestFixture]
    public class NodeInitialize
    {
        rcl_context_t context = new rcl_context_t();

        [SetUp]
        public void SetUp()
        {
            RCLInitialize.InitRcl(ref context);
        }

        [TearDown]
        public void TearDown()
        {
            RCLInitialize.ShutdownRcl(ref context);
        }

        [Test]
        public void GetZeroInitializedNode()
        {
            rcl_node_t node = NativeRcl.rcl_get_zero_initialized_node();
            Assert.That(node, Is.EqualTo(default(rcl_node_t)));
        }

        [Test]
        public void NodeGetDefaultOptions()
        {
            IntPtr defaultNodeOptions = NativeRclInterface.rclcs_node_create_default_options();
            Assert.That(defaultNodeOptions, Is.Not.EqualTo(IntPtr.Zero));
            TestUtils.AssertRetOk(NativeRclInterface.rclcs_node_dispose_options(defaultNodeOptions));
        }

        [Test]
        public void NodeOptionsSetEnableRosout()
        {
            IntPtr defaultNodeOptions = NativeRclInterface.rclcs_node_create_default_options();
            Assert.That(defaultNodeOptions, Is.Not.EqualTo(IntPtr.Zero));
            try
            {
                TestUtils.AssertRetOk(NativeRclInterface.rclcs_node_options_set_enable_rosout(defaultNodeOptions, false));
            }
            finally
            {
                TestUtils.AssertRetOk(NativeRclInterface.rclcs_node_dispose_options(defaultNodeOptions));
            }
        }

        [Test]
        public void NodeDisposeOptionsAcceptsNull()
        {
            TestUtils.AssertRetOk(NativeRclInterface.rclcs_node_dispose_options(IntPtr.Zero));
        }

        public static void InitNode(ref rcl_node_t node, ref IntPtr nodeOptions, ref rcl_context_t context)
        {
            node = NativeRcl.rcl_get_zero_initialized_node();

            nodeOptions = NativeRclInterface.rclcs_node_create_default_options();
            Assert.That(nodeOptions, Is.Not.EqualTo(IntPtr.Zero));
            string name = "node_test";
            string nodeNamespace = "/ns";

            var ret = (RCLReturnEnum)NativeRcl.rcl_node_init(
                ref node, name, nodeNamespace, ref context, nodeOptions);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK));
        }

        public static void ShutdownNode(ref rcl_node_t node, IntPtr nodeOptions)
        {
            TestUtils.AssertRetOk(NativeRcl.rcl_node_fini(ref node));
            if (nodeOptions != IntPtr.Zero)
            {
                TestUtils.AssertRetOk(NativeRclInterface.rclcs_node_dispose_options(nodeOptions));
            }
        }

        [Test]
        public void NodeInitShutdown()
        {
            rcl_node_t node = new rcl_node_t();
            IntPtr nodeOptions = new IntPtr();

            InitNode(ref node, ref nodeOptions, ref context);
            ShutdownNode(ref node, nodeOptions);
        }
    }

    [TestFixture]
    public class Node
    {
        rcl_context_t context;
        rcl_node_t node;
        IntPtr nodeOptions = new IntPtr();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RCLInitialize.InitRcl(ref context);
            NodeInitialize.InitNode(ref node, ref nodeOptions, ref context);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            NodeInitialize.ShutdownNode(ref node, nodeOptions);
            RCLInitialize.ShutdownRcl(ref context);
        }

        [Test]
        public void NodeGetName()
        {
            string nodeNameFromRcl = Utils.PtrToString(NativeRcl.rcl_node_get_name(ref node));
            Assert.That("node_test", Is.EqualTo(nodeNameFromRcl));
        }

        [Test]
        public void NodeGetNamespace()
        {
            string nodeNamespaceFromRcl = Utils.PtrToString(NativeRcl.rcl_node_get_namespace(ref node));
            Assert.That("/ns", Is.EqualTo(nodeNamespaceFromRcl));
        }

        [Test]
        public void CountPublishersAndSubscribersOnEmptyTopic()
        {
            UIntPtr count = UIntPtr.Zero;
            TestUtils.AssertRetOk(NativeRcl.rcl_count_publishers(
                ref node,
                "/native_graph_count_empty_topic",
                ref count));
            Assert.That(count, Is.EqualTo(UIntPtr.Zero));

            TestUtils.AssertRetOk(NativeRcl.rcl_count_subscribers(
                ref node,
                "/native_graph_count_empty_topic",
                ref count));
            Assert.That(count, Is.EqualTo(UIntPtr.Zero));
        }

        [Test]
        public void GetTopicNamesAndTypesReturnsOkAndNonNull()
        {
            IntPtr result = IntPtr.Zero;
            try
            {
                TestUtils.AssertRetOk(NativeRclInterface.rclcs_get_topic_names_and_types(
                    ref node,
                    false,
                    out result));
                Assert.That(result, Is.Not.EqualTo(IntPtr.Zero));
            }
            finally
            {
                NativeRclInterface.rclcs_dispose_topic_names_and_types(result);
            }
        }

        [Test]
        public void DisposeTopicNamesAndTypesAcceptsNull()
        {
            Assert.DoesNotThrow(() => NativeRclInterface.rclcs_dispose_topic_names_and_types(IntPtr.Zero));
        }

        [Test]
        public void GetZeroInitializedClientAndService()
        {
            rcl_client_t client = NativeRcl.rcl_get_zero_initialized_client();
            rcl_service_t service = NativeRcl.rcl_get_zero_initialized_service();

            Assert.That(client, Is.EqualTo(default(rcl_client_t)));
            Assert.That(service, Is.EqualTo(default(rcl_service_t)));
        }

        [Test]
        public void ClientAndServiceCreateOptions()
        {
            using (QualityOfServiceProfile qos = new QualityOfServiceProfile(QosPresetProfile.SERVICES_DEFAULT))
            {
                IntPtr clientOptions = NativeRclInterface.rclcs_client_create_options(qos.handle);
                IntPtr serviceOptions = NativeRclInterface.rclcs_service_create_options(qos.handle);
                try
                {
                    Assert.That(clientOptions, Is.Not.EqualTo(IntPtr.Zero));
                    Assert.That(serviceOptions, Is.Not.EqualTo(IntPtr.Zero));
                }
                finally
                {
                    NativeRclInterface.rclcs_client_dispose_options(clientOptions);
                    NativeRclInterface.rclcs_service_dispose_options(serviceOptions);
                }
            }
        }

        [Test]
        public void ClientAndServiceDisposeOptionsAcceptNull()
        {
            Assert.DoesNotThrow(() => NativeRclInterface.rclcs_client_dispose_options(IntPtr.Zero));
            Assert.DoesNotThrow(() => NativeRclInterface.rclcs_service_dispose_options(IntPtr.Zero));
        }

        [Test]
        public void ServiceServerIsAvailableMarshalsBoolOutput()
        {
            rcl_client_t client = NativeRcl.rcl_get_zero_initialized_client();
            rcl_service_t service = NativeRcl.rcl_get_zero_initialized_service();
            IntPtr clientOptions = IntPtr.Zero;
            IntPtr serviceOptions = IntPtr.Zero;
            bool clientInitialized = false;
            bool serviceInitialized = false;

            using (QualityOfServiceProfile qos = new QualityOfServiceProfile(QosPresetProfile.SERVICES_DEFAULT))
            using (AddTwoInts_Request request = new AddTwoInts_Request())
            {
                MessageInternals requestInternals = request;
                IntPtr typeSupportHandle = requestInternals.TypeSupportHandle;
                clientOptions = NativeRclInterface.rclcs_client_create_options(qos.handle);
                serviceOptions = NativeRclInterface.rclcs_service_create_options(qos.handle);
                Assert.That(clientOptions, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(serviceOptions, Is.Not.EqualTo(IntPtr.Zero));
                try
                {
                    TestUtils.AssertRetOk(NativeRcl.rcl_service_init(
                        ref service,
                        ref node,
                        typeSupportHandle,
                        "native_service_available_test",
                        serviceOptions));
                    serviceInitialized = true;

                    TestUtils.AssertRetOk(NativeRcl.rcl_client_init(
                        ref client,
                        ref node,
                        typeSupportHandle,
                        "native_service_available_test",
                        clientOptions));
                    clientInitialized = true;

                    bool isAvailable = false;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(2);
                    do
                    {
                        TestUtils.AssertRetOk(NativeRcl.rcl_service_server_is_available(
                            ref node,
                            ref client,
                            ref isAvailable));
                        if (isAvailable)
                        {
                            break;
                        }
                        Thread.Sleep(10);
                    } while (DateTime.UtcNow < deadline);

                    Assert.That(isAvailable, Is.True);
                }
                finally
                {
                    if (clientInitialized)
                    {
                        TestUtils.AssertRetOk(NativeRcl.rcl_client_fini(ref client, ref node));
                    }
                    if (serviceInitialized)
                    {
                        TestUtils.AssertRetOk(NativeRcl.rcl_service_fini(ref service, ref node));
                    }
                    if (clientOptions != IntPtr.Zero)
                    {
                        NativeRclInterface.rclcs_client_dispose_options(clientOptions);
                    }
                    if (serviceOptions != IntPtr.Zero)
                    {
                        NativeRclInterface.rclcs_service_dispose_options(serviceOptions);
                    }
                }
            }
        }

        [Test]
        public void ClientServiceRequestResponseNativeRoundtrip()
        {
            rcl_client_t client = NativeRcl.rcl_get_zero_initialized_client();
            rcl_service_t service = NativeRcl.rcl_get_zero_initialized_service();
            IntPtr clientOptions = IntPtr.Zero;
            IntPtr serviceOptions = IntPtr.Zero;
            bool clientInitialized = false;
            bool serviceInitialized = false;

            using (QualityOfServiceProfile qos = new QualityOfServiceProfile(QosPresetProfile.SERVICES_DEFAULT))
            using (AddTwoInts_Request request = new AddTwoInts_Request { A = 4, B = 5 })
            using (AddTwoInts_Request takenRequest = new AddTwoInts_Request())
            using (AddTwoInts_Response response = new AddTwoInts_Response())
            using (AddTwoInts_Response takenResponse = new AddTwoInts_Response())
            {
                MessageInternals requestInternals = request;
                IntPtr typeSupportHandle = requestInternals.TypeSupportHandle;
                clientOptions = NativeRclInterface.rclcs_client_create_options(qos.handle);
                serviceOptions = NativeRclInterface.rclcs_service_create_options(qos.handle);
                Assert.That(clientOptions, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(serviceOptions, Is.Not.EqualTo(IntPtr.Zero));
                try
                {
                    TestUtils.AssertRetOk(NativeRcl.rcl_service_init(
                        ref service,
                        ref node,
                        typeSupportHandle,
                        "native_client_service_roundtrip_test",
                        serviceOptions));
                    serviceInitialized = true;

                    TestUtils.AssertRetOk(NativeRcl.rcl_client_init(
                        ref client,
                        ref node,
                        typeSupportHandle,
                        "native_client_service_roundtrip_test",
                        clientOptions));
                    clientInitialized = true;

                    AssertClientAndServiceCanBeAddedToWaitSet(ref client, ref service);
                    AssertServiceAvailable(ref client);

                    long sequenceNumber = 0;
                    request.WriteNativeMessage();
                    TestUtils.AssertRetOk(NativeRcl.rcl_send_request(
                        ref client,
                        requestInternals.Handle,
                        ref sequenceNumber));

                    rcl_rmw_request_id_t requestHeader = default(rcl_rmw_request_id_t);
                    MessageInternals takenRequestInternals = takenRequest;
                    AssertEventuallyReturns(
                        () => NativeRcl.rcl_take_request(
                            ref service,
                            ref requestHeader,
                            takenRequestInternals.Handle),
                        RCLReturnEnum.RCL_RET_SERVICE_TAKE_FAILED);
                    takenRequest.ReadNativeMessage();
                    Assert.That(takenRequest.A, Is.EqualTo(4));
                    Assert.That(takenRequest.B, Is.EqualTo(5));

                    response.Sum = takenRequest.A + takenRequest.B;
                    MessageInternals responseInternals = response;
                    response.WriteNativeMessage();
                    TestUtils.AssertRetOk(NativeRcl.rcl_send_response(
                        ref service,
                        ref requestHeader,
                        responseInternals.Handle));

                    rcl_rmw_request_id_t responseHeader = default(rcl_rmw_request_id_t);
                    MessageInternals takenResponseInternals = takenResponse;
                    AssertEventuallyReturns(
                        () => NativeRcl.rcl_take_response(
                            ref client,
                            ref responseHeader,
                            takenResponseInternals.Handle),
                        RCLReturnEnum.RCL_RET_CLIENT_TAKE_FAILED);
                    Assert.That(responseHeader.sequence_number, Is.EqualTo(sequenceNumber));
                    takenResponse.ReadNativeMessage();
                    Assert.That(takenResponse.Sum, Is.EqualTo(9));
                }
                finally
                {
                    if (clientInitialized)
                    {
                        TestUtils.AssertRetOk(NativeRcl.rcl_client_fini(ref client, ref node));
                    }
                    if (serviceInitialized)
                    {
                        TestUtils.AssertRetOk(NativeRcl.rcl_service_fini(ref service, ref node));
                    }
                    if (clientOptions != IntPtr.Zero)
                    {
                        NativeRclInterface.rclcs_client_dispose_options(clientOptions);
                    }
                    if (serviceOptions != IntPtr.Zero)
                    {
                        NativeRclInterface.rclcs_service_dispose_options(serviceOptions);
                    }
                }
            }
        }

        private static void AssertEventuallyReturns(Func<int> nativeCall, RCLReturnEnum retryCode)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            RCLReturnEnum ret;
            do
            {
                ret = (RCLReturnEnum)nativeCall();
                if (ret == RCLReturnEnum.RCL_RET_OK)
                {
                    return;
                }
                if (ret != retryCode)
                {
                    Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK), Utils.PopRclErrorString());
                }
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);

            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK), Utils.PopRclErrorString());
        }

        private void AssertServiceAvailable(ref rcl_client_t client)
        {
            bool isAvailable = false;
            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            do
            {
                TestUtils.AssertRetOk(NativeRcl.rcl_service_server_is_available(
                    ref node,
                    ref client,
                    ref isAvailable));
                if (isAvailable)
                {
                    return;
                }
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);

            Assert.That(isAvailable, Is.True);
        }

        private void AssertClientAndServiceCanBeAddedToWaitSet(
            ref rcl_client_t client,
            ref rcl_service_t service)
        {
            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
            rcl_wait_set_t waitSet = NativeRcl.rcl_get_zero_initialized_wait_set();
            bool waitSetInitialized = false;
            try
            {
                TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_init(
                    ref waitSet,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)1,
                    (UIntPtr)1,
                    (UIntPtr)0,
                    ref context,
                    allocator));
                waitSetInitialized = true;
                TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_clear(ref waitSet));

                UIntPtr clientIndex = (UIntPtr)42;
                UIntPtr serviceIndex = (UIntPtr)42;
                TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_add_client(ref waitSet, ref client, ref clientIndex));
                TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_add_service(ref waitSet, ref service, ref serviceIndex));
                Assert.That(clientIndex.ToUInt64(), Is.EqualTo(0));
                Assert.That(serviceIndex.ToUInt64(), Is.EqualTo(0));
            }
            finally
            {
                if (waitSetInitialized)
                {
                    TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_fini(ref waitSet));
                }
            }
        }
    }

    [TestFixture]
    public class PublisherInitialize
    {
        rcl_context_t context;
        rcl_node_t node;
        IntPtr nodeOptions = new IntPtr();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RCLInitialize.InitRcl(ref context);
            NodeInitialize.InitNode(ref node, ref nodeOptions, ref context);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            NodeInitialize.ShutdownNode(ref node, nodeOptions);
            RCLInitialize.ShutdownRcl(ref context);
        }

        [Test]
        public void PublisherCreateOptions()
        {
            using (QualityOfServiceProfile qos = new QualityOfServiceProfile())
            {
                IntPtr publisherOptions = NativeRclInterface.rclcs_publisher_create_options(qos.handle);
                Assert.That(publisherOptions, Is.Not.EqualTo(IntPtr.Zero));
                NativeRclInterface.rclcs_publisher_dispose_options(publisherOptions);
            }
        }

        [Test]
        public void GetZeroInitializedPublisher()
        {
            rcl_publisher_t publisher = NativeRcl.rcl_get_zero_initialized_publisher();
            Assert.That(publisher, Is.EqualTo(default(rcl_publisher_t)));
        }

        public static void InitPublisher(
            ref rcl_publisher_t publisher, ref rcl_node_t node, ref IntPtr publisherOptions)
        {
            publisher = NativeRcl.rcl_get_zero_initialized_publisher();
            using (QualityOfServiceProfile qos = new QualityOfServiceProfile())
            {
                publisherOptions = NativeRclInterface.rclcs_publisher_create_options(qos.handle);
            }
            Assert.That(publisherOptions, Is.Not.EqualTo(IntPtr.Zero));
            using var msg = new std_msgs.msg.Bool();
            MessageInternals msgInternals = msg;
            IntPtr typeSupportHandle = msgInternals.TypeSupportHandle;
            var ret = (RCLReturnEnum)NativeRcl.rcl_publisher_init(
                ref publisher, ref node, typeSupportHandle, "publisher_test_topic", publisherOptions);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK), Utils.PopRclErrorString());
        }

        public static void ShutdownPublisher(
            ref rcl_publisher_t publisher, ref rcl_node_t node, IntPtr publisherOptions)
        {
            var ret = (RCLReturnEnum)NativeRcl.rcl_publisher_fini(ref publisher, ref node);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK), Utils.PopRclErrorString());
            if (publisherOptions != IntPtr.Zero)
            {
                NativeRclInterface.rclcs_publisher_dispose_options(publisherOptions);
            }
        }

        [Test]
        public void PublisherInit()
        {
            rcl_publisher_t publisher = new rcl_publisher_t();
            IntPtr publisherOptions = new IntPtr();
            InitPublisher(ref publisher, ref node, ref publisherOptions);
            ShutdownPublisher(ref publisher, ref node, publisherOptions);
        }
    }

    [TestFixture]
    public class Publisher
    {
        rcl_context_t context;
        rcl_node_t node;
        IntPtr nodeOptions = new IntPtr();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RCLInitialize.InitRcl(ref context);
            NodeInitialize.InitNode(ref node, ref nodeOptions, ref context);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            NodeInitialize.ShutdownNode(ref node, nodeOptions);
            RCLInitialize.ShutdownRcl(ref context);
        }

        [Test]
        public void PublisherPublish()
        {
            rcl_publisher_t publisher = new rcl_publisher_t();
            IntPtr publisherOptions = new IntPtr();
            PublisherInitialize.InitPublisher(ref publisher, ref node, ref publisherOptions);
            using var msg = new std_msgs.msg.Bool();

            var ret = (RCLReturnEnum)NativeRcl.rcl_publish(ref publisher, msg.Handle, IntPtr.Zero);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK), Utils.PopRclErrorString());
            PublisherInitialize.ShutdownPublisher(ref publisher, ref node, publisherOptions);
        }
    }

    [TestFixture]
    public class SubscriptionInitialize
    {
        [Test]
        public void GetZeroInitializedSubscription()
        {
            rcl_subscription_t subscription = NativeRcl.rcl_get_zero_initialized_subscription();
            Assert.That(subscription, Is.EqualTo(default(rcl_subscription_t)));
        }

        [Test]
        public void SubscriptionCreateOptions()
        {
            using (QualityOfServiceProfile qos = new QualityOfServiceProfile())
            {
                IntPtr subscriptionOptions = NativeRclInterface.rclcs_subscription_create_options(qos.handle);
                Assert.That(subscriptionOptions, Is.Not.EqualTo(IntPtr.Zero));
                TestUtils.AssertRetOk(NativeRclInterface.rclcs_subscription_dispose_options(subscriptionOptions));
            }
        }

        [Test]
        public void SubscriptionDisposeOptionsAcceptsNull()
        {
            TestUtils.AssertRetOk(NativeRclInterface.rclcs_subscription_dispose_options(IntPtr.Zero));
        }

        public static void InitSubscription(
            ref rcl_subscription_t subscription, ref IntPtr subscriptionOptions, ref rcl_node_t node)
        {
            subscription = NativeRcl.rcl_get_zero_initialized_subscription();
            using (QualityOfServiceProfile qos = new QualityOfServiceProfile())
            {
                subscriptionOptions = NativeRclInterface.rclcs_subscription_create_options(qos.handle);
            }
            Assert.That(subscriptionOptions, Is.Not.EqualTo(IntPtr.Zero));
            using var msg = new std_msgs.msg.Bool();
            MessageInternals msgInternals = msg;
            IntPtr typeSupportHandle = msgInternals.TypeSupportHandle;
            var ret = (RCLReturnEnum)NativeRcl.rcl_subscription_init(
                ref subscription, ref node, typeSupportHandle, "/subscriber_test_topic", subscriptionOptions);
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK), Utils.PopRclErrorString());
        }

        public static void ShutdownSubscription(
            ref rcl_subscription_t subscription, IntPtr subscriptionOptions, ref rcl_node_t node)
        {
            var ret = (RCLReturnEnum)NativeRcl.rcl_subscription_fini(ref subscription, ref node);
            if (subscriptionOptions != IntPtr.Zero)
            {
                TestUtils.AssertRetOk(NativeRclInterface.rclcs_subscription_dispose_options(subscriptionOptions));
            }
            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_OK), Utils.PopRclErrorString());
        }

        [Test]
        public void SubscriptionInit()
        {
            rcl_context_t context = new rcl_context_t();
            rcl_node_t node = new rcl_node_t();
            IntPtr nodeOptions = new IntPtr();

            RCLInitialize.InitRcl(ref context);
            NodeInitialize.InitNode(ref node, ref nodeOptions, ref context);

            rcl_subscription_t subscription = new rcl_subscription_t();
            IntPtr subscriptionOptions = new IntPtr();

            InitSubscription(ref subscription, ref subscriptionOptions, ref node);
            ShutdownSubscription(ref subscription, subscriptionOptions, ref node);

            NodeInitialize.ShutdownNode(ref node, nodeOptions);
            RCLInitialize.ShutdownRcl(ref context);
        }
    }

    [TestFixture]
    public class Subscription
    {
        rcl_context_t context;
        rcl_node_t node;
        IntPtr nodeOptions = new IntPtr();
        rcl_subscription_t subscription;
        IntPtr subscriptionOptions = new IntPtr();

        [SetUp]
        public void SetUp()
        {
            RCLInitialize.InitRcl(ref context);
            NodeInitialize.InitNode(ref node, ref nodeOptions, ref context);
            SubscriptionInitialize.InitSubscription(ref subscription, ref subscriptionOptions, ref node);
        }

        [TearDown]
        public void TearDown()
        {
            SubscriptionInitialize.ShutdownSubscription(ref subscription, subscriptionOptions, ref node);
            NodeInitialize.ShutdownNode(ref node, nodeOptions);
            RCLInitialize.ShutdownRcl(ref context);
        }

        [Test]
        public void SubscriptionIsValid()
        {
            Assert.That(NativeRcl.rcl_subscription_is_valid(ref subscription), Is.True);
        }

        [Test]
        public void SubscriptionTakeEmptyReturnsTakeFailed()
        {
            using var msg = new std_msgs.msg.Bool();
            MessageInternals msgInternals = msg;

            var ret = (RCLReturnEnum)NativeRcl.rcl_take(
                ref subscription,
                msgInternals.Handle,
                IntPtr.Zero,
                IntPtr.Zero);

            Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_SUBSCRIPTION_TAKE_FAILED));
        }

        [Test]
        public void WaitSetAddSubscription()
        {
            NativeRcl.rcl_reset_error();

            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
            rcl_wait_set_t waitSet = NativeRcl.rcl_get_zero_initialized_wait_set();
            bool waitSetInitialized = false;
            try
            {
                TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_init(
                    ref waitSet,
                    (UIntPtr)1,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    ref context,
                    allocator
                ));
                waitSetInitialized = true;
                TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_clear(ref waitSet));

                Assert.That(NativeRcl.rcl_subscription_is_valid(ref subscription), Is.True);
                UIntPtr index = (UIntPtr)42;
                TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_add_subscription(ref waitSet, ref subscription, ref index));
                Assert.That(index.ToUInt64(), Is.EqualTo(0));

                long timeout_ns = 10 * 1000 * 1000; // 10 ms expressed in rcl_wait nanoseconds.
                var ret = (RCLReturnEnum)NativeRcl.rcl_wait(ref waitSet, timeout_ns);
                Assert.That(ret, Is.EqualTo(RCLReturnEnum.RCL_RET_TIMEOUT));
            }
            finally
            {
                if (waitSetInitialized)
                {
                    TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_fini(ref waitSet));
                }
            }
        }
    }

    [TestFixture]
    public class WaitSet
    {
        rcl_context_t context;
        rcl_node_t node;
        IntPtr nodeOptions = new IntPtr();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RCLInitialize.InitRcl(ref context);
            NodeInitialize.InitNode(ref node, ref nodeOptions, ref context);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            NodeInitialize.ShutdownNode(ref node, nodeOptions);
            RCLInitialize.ShutdownRcl(ref context);
        }

        [Test]
        public void GetZeroInitializedWaitSet()
        {
            // NOTE: The struct rcl_wait_set_t contains size_t
            // fields that are set to UIntPtr in C# declaration,
            // not guaranteed to work for all C implemenations/platforms.
            rcl_wait_set_t waitSet = NativeRcl.rcl_get_zero_initialized_wait_set();
            Assert.That(waitSet, Is.EqualTo(default(rcl_wait_set_t)));
        }

        [Test]
        public void WaitSetInit()
        {
            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
            rcl_wait_set_t waitSet = NativeRcl.rcl_get_zero_initialized_wait_set();
            TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_init(
                ref waitSet,
                (UIntPtr)1,
                (UIntPtr)0,
                (UIntPtr)0,
                (UIntPtr)0,
                (UIntPtr)0,
                (UIntPtr)0,
                ref context,
                allocator
            ));
            TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_fini(ref waitSet));
        }

        [Test]
        public void WaitSetClear()
        {
            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
            rcl_wait_set_t waitSet = NativeRcl.rcl_get_zero_initialized_wait_set();
            bool waitSetInitialized = false;
            try
            {
                TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_init(
                    ref waitSet,
                    (UIntPtr)1,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    ref context,
                    allocator
                ));
                waitSetInitialized = true;
                TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_clear(ref waitSet));
            }
            finally
            {
                if (waitSetInitialized)
                {
                    TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_fini(ref waitSet));
                }
            }
        }

        [Test]
        public void WaitSetResize()
        {
            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
            rcl_wait_set_t waitSet = NativeRcl.rcl_get_zero_initialized_wait_set();
            bool waitSetInitialized = false;
            try
            {
                TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_init(
                    ref waitSet,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    ref context,
                    allocator
                ));
                waitSetInitialized = true;

                TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_resize(
                    ref waitSet,
                    (UIntPtr)1,
                    (UIntPtr)0,
                    (UIntPtr)0,
                    (UIntPtr)1,
                    (UIntPtr)1,
                    (UIntPtr)0));
                Assert.That(waitSet.size_of_subscriptions.ToUInt64(), Is.EqualTo(1));
                Assert.That(waitSet.size_of_clients.ToUInt64(), Is.EqualTo(1));
                Assert.That(waitSet.size_of_services.ToUInt64(), Is.EqualTo(1));
            }
            finally
            {
                if (waitSetInitialized)
                {
                    TestUtils.AssertRetOk(NativeRcl.rcl_wait_set_fini(ref waitSet));
                }
            }
        }
    }

    [TestFixture]
    public class QualityOfService
    {
        rcl_context_t context;
        rcl_node_t node;
        IntPtr nodeOptions = new IntPtr();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RCLInitialize.InitRcl(ref context);
            NodeInitialize.InitNode(ref node, ref nodeOptions, ref context);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            NodeInitialize.ShutdownNode(ref node, nodeOptions);
            RCLInitialize.ShutdownRcl(ref context);
        }

        [Test]
        public void SetSubscriptionQosProfile()
        {
            rcl_subscription_t subscription = NativeRcl.rcl_get_zero_initialized_subscription();

            using (QualityOfServiceProfile qos = new QualityOfServiceProfile())
            {
                IntPtr subscriptionOptions = NativeRclInterface.rclcs_subscription_create_options(qos.handle);
                Assert.That(subscriptionOptions, Is.Not.EqualTo(IntPtr.Zero));
                bool subscriptionInitialized = false;
                try
                {
                    using var msg = new std_msgs.msg.Bool();
                    MessageInternals msgInternals = msg;
                    IntPtr typeSupportHandle = msgInternals.TypeSupportHandle;
                    TestUtils.AssertRetOk(NativeRcl.rcl_subscription_init(
                        ref subscription, ref node, typeSupportHandle, "/subscriber_test_topic", subscriptionOptions));
                    subscriptionInitialized = true;

                    Assert.That(NativeRcl.rcl_subscription_is_valid(ref subscription), Is.True);
                }
                finally
                {
                    if (subscriptionInitialized)
                    {
                        TestUtils.AssertRetOk(NativeRcl.rcl_subscription_fini(ref subscription, ref node));
                    }
                    TestUtils.AssertRetOk(NativeRclInterface.rclcs_subscription_dispose_options(subscriptionOptions));
                }
            }
        }
    }

    [TestFixture]
    public class QualityOfServiceProfileMethods
    {
        [Test]
        public void QosPolicyEnumsMatchRmwOrdinals()
        {
            Assert.That((int)HistoryPolicy.QOS_POLICY_HISTORY_SYSTEM_DEFAULT, Is.EqualTo(0));
            Assert.That((int)HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST, Is.EqualTo(1));
            Assert.That((int)HistoryPolicy.QOS_POLICY_HISTORY_KEEP_ALL, Is.EqualTo(2));
            Assert.That((int)ReliabilityPolicy.QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT, Is.EqualTo(0));
            Assert.That((int)ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE, Is.EqualTo(1));
            Assert.That((int)ReliabilityPolicy.QOS_POLICY_RELIABILITY_BEST_EFFORT, Is.EqualTo(2));
            Assert.That((int)DurabilityPolicy.QOS_POLICY_DURABILITY_SYSTEM_DEFAULT, Is.EqualTo(0));
            Assert.That((int)DurabilityPolicy.QOS_POLICY_DURABILITY_TRANSIENT_LOCAL, Is.EqualTo(1));
            Assert.That((int)DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE, Is.EqualTo(2));
            Assert.That((int)LivelinessPolicy.QOS_POLICY_LIVELINESS_SYSTEM_DEFAULT, Is.EqualTo(0));
            Assert.That((int)LivelinessPolicy.QOS_POLICY_LIVELINESS_AUTOMATIC, Is.EqualTo(1));
            // Ordinal 2 was MANUAL_BY_NODE in older ROS 2/RMW headers; MANUAL_BY_TOPIC remains 3.
            Assert.That((int)LivelinessPolicy.QOS_POLICY_LIVELINESS_MANUAL_BY_TOPIC, Is.EqualTo(3));
        }

        [Test]
        public void InvalidQosPresetThrows()
        {
            Assert.Throws<RuntimeError>(
                () =>
                {
                    using var qos = new QualityOfServiceProfile((QosPresetProfile)999);
                });
        }

        [Test]
        public void QosPresetProfilesCreateNativeHandles()
        {
            foreach (QosPresetProfile preset in Enum.GetValues(typeof(QosPresetProfile)))
            {
                using var qos = new QualityOfServiceProfile(preset);
                Assert.That(qos.handle, Is.Not.EqualTo(IntPtr.Zero), preset.ToString());
            }
        }

        [Test]
        public void HistoryRejectsNegativeDepthAndKeepLastRejectsZero()
        {
            using var qos = new QualityOfServiceProfile();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => qos.SetHistory(HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => qos.SetHistory(HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST, -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => qos.SetHistory(HistoryPolicy.QOS_POLICY_HISTORY_KEEP_ALL, -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => qos.SetHistory(HistoryPolicy.QOS_POLICY_HISTORY_SYSTEM_DEFAULT, -1));
            Assert.DoesNotThrow(
                () => qos.SetHistory(HistoryPolicy.QOS_POLICY_HISTORY_KEEP_ALL, 0));
        }

        [Test]
        public void SetLivelinessPolicyDoesNotThrow()
        {
            using var qos = new QualityOfServiceProfile();

            Assert.DoesNotThrow(
                () => qos.SetLiveliness(LivelinessPolicy.QOS_POLICY_LIVELINESS_AUTOMATIC));
        }

        [Test]
        public void SetDurationPoliciesDoNotThrow()
        {
            using var qos = new QualityOfServiceProfile();

            Assert.DoesNotThrow(() => qos.SetDeadline(TimeSpan.FromMilliseconds(20)));
            Assert.DoesNotThrow(() => qos.SetLifespan(TimeSpan.FromSeconds(1)));
            Assert.DoesNotThrow(() => qos.SetLivelinessLeaseDuration(TimeSpan.FromMilliseconds(200)));
        }

        [Test]
        public void SetDurationPoliciesRejectNegativeValues()
        {
            using var qos = new QualityOfServiceProfile();

            Assert.Throws<ArgumentOutOfRangeException>(() => qos.SetDeadline(TimeSpan.FromTicks(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => qos.SetLifespan(TimeSpan.FromTicks(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => qos.SetLivelinessLeaseDuration(TimeSpan.FromTicks(-1)));
        }

        [Test]
        public void SetPoliciesDoesNotThrow()
        {
            using var qos = new QualityOfServiceProfile();

            Assert.DoesNotThrow(
                () => qos.SetPolicies(
                    HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
                    10,
                    ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE,
                    DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE));
        }
    }

    [TestFixture]
    public class Clock
    {
        rcl_context_t context;
        rcl_node_t node;
        IntPtr nodeOptions = new IntPtr();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RCLInitialize.InitRcl(ref context);
            NodeInitialize.InitNode(ref node, ref nodeOptions, ref context);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            NodeInitialize.ShutdownNode(ref node, nodeOptions);
            RCLInitialize.ShutdownRcl(ref context);
        }

        [Test]
        public void CreateClock()
        {
            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
            IntPtr clockHandle = NativeRclInterface.rclcs_ros_clock_create(ref allocator);
            Assert.That(clockHandle, Is.Not.EqualTo(IntPtr.Zero));
            NativeRclInterface.rclcs_ros_clock_dispose(clockHandle);
        }

        [Test]
        public void ClockGetNow()
        {
            rcl_allocator_t allocator = NativeRcl.rcutils_get_default_allocator();
            IntPtr clockHandle = NativeRclInterface.rclcs_ros_clock_create(ref allocator);
            long queryNow = 0;
            NativeRcl.rcl_clock_get_now(clockHandle, ref queryNow);

            Assert.That(queryNow, Is.Not.EqualTo(0));

            NativeRclInterface.rclcs_ros_clock_dispose(clockHandle);
        }
    }
}
