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
// - Corrected Remove* documentation to match the implementation's disposal behavior.

using System;
using System.Collections.Generic;

namespace ROS2
{
  /// <summary> Ros2cs node, created with Ros2cs.CreateNode and supporting use of publishers and subscribers </summary>
  /// <description> Is automatically disposed when Ros2cs.Shutdown is called.
  /// Can also be disposed through IDisposable interface. Ros2cs.RemoveNode should be called in such case </description>
  // TODO(adamdbrw) wrap disposing so that user does not need to handle anything
  public interface INode: IExtendedDisposable
  {
    /// <summary> Node name as given in Ros2cs.CreateNode </summary>
    string Name {get;}

    /// <summary> Count publishers currently visible for a topic in this node's local ROS graph cache. </summary>
    /// <description> The ROS graph is updated asynchronously by DDS; callers may need to poll after endpoint creation or teardown. </description>
    /// <param name="topicName"> Fully qualified topic name passed through to rcl. Naming restrictions of ros2 apply and violation results in an exception. </param>
    /// <returns> Number of visible publishers for the topic. </returns>
    int CountPublishers(string topicName);

    /// <summary> Count subscribers currently visible for a topic in this node's local ROS graph cache. </summary>
    /// <description> The ROS graph is updated asynchronously by DDS; callers may need to poll after endpoint creation or teardown. </description>
    /// <param name="topicName"> Fully qualified topic name passed through to rcl. Naming restrictions of ros2 apply and violation results in an exception. </param>
    /// <returns> Number of visible subscribers for the topic. </returns>
    int CountSubscribers(string topicName);

    /// <summary> Wait up to a bounded timeout for a publisher to become visible for a topic. </summary>
    /// <description> Polls this node's local ROS graph cache and returns false on timeout. The topic name is passed through to rcl without normalization. Throws the same node usability exceptions as CountPublishers. </description>
    /// <param name="topicName"> Topic name passed through to rcl. </param>
    /// <param name="timeout"> Maximum time to wait. </param>
    /// <returns> Whether at least one publisher became visible before timeout. </returns>
    bool TryWaitForPublisher(string topicName, TimeSpan timeout);

    /// <summary> Wait up to a bounded timeout for a subscriber to become visible for a topic. </summary>
    /// <description> Polls this node's local ROS graph cache and returns false on timeout. The topic name is passed through to rcl without normalization. Throws the same node usability exceptions as CountSubscribers. </description>
    /// <param name="topicName"> Topic name passed through to rcl. </param>
    /// <param name="timeout"> Maximum time to wait. </param>
    /// <returns> Whether at least one subscriber became visible before timeout. </returns>
    bool TryWaitForSubscriber(string topicName, TimeSpan timeout);

    /// <summary> Get topic names and type names currently visible in this node's local ROS graph cache. </summary>
    /// <description> The ROS graph is updated asynchronously by DDS; callers may need to poll after endpoint creation or teardown. </description>
    /// <param name="noDemangle"> Whether rcl should skip ROS topic name demangling. Defaults to false to match ROS graph tooling. </param>
    /// <returns> Visible topic names with their associated type names. </returns>
    IReadOnlyList<TopicNamesAndTypes> GetTopicNamesAndTypes(bool noDemangle = false);

    /// <summary> Create a client for this node for a given topic, qos and message type </summary>
    /// <description> Can only be called in an initialized Ros2cs state. </description>
    /// <param name="topic"> Topic for the client. Naming restrictions of ros2 apply and violation results in an exception </param>
    /// <param name="qos"> Quality of Client settings. Not passing this parameter will result in default settings </param>
    /// <returns> Client for the topic, which can be used to send service requests </returns>
    Client<I, O> CreateClient<I, O>(string topic, QualityOfServiceProfile qos = null) where I : Message, new() where O : Message, new();

    /// <summary> Remove a client </summary>
    /// <remarks> Removes the client from the node and calls Dispose on it. </remarks>
    /// <param name="client"> Client created with earlier CreateClient call </param>
    /// <returns> Whether removal actually took place. Safe to ignore </returns>
    bool RemoveClient(IClientBase client);

    /// <summary> Create a service for this node for a given topic, callback, qos and message type </summary>
    /// <description> Can only be called in an initialized Ros2cs state. </description>
    /// <param name="topic"> Topic to service to. Naming restrictions of ros2 apply and violation results in an exception </param>
    /// <param name="callback"> Action to be called when message is received (through Spin or SpinOnce). The message passed to this callback is owned and disposed by ros2cs. Provide a lambda or a method </param>
    /// <param name="qos"> Quality of Service settings. Not passing this parameter will result in default settings </param>
    /// <returns> Service for the topic </returns>
    Service<I, O> CreateService<I, O>(string topic, Func<I, O> callback, QualityOfServiceProfile qos = null) where I : Message, new() where O : Message, new();

    /// <summary> Remove a service </summary>
    /// <remarks> Removes the service from the node and calls Dispose on it. </remarks>
    /// <param name="service"> Service created with earlier CreateService call </param>
    /// <returns> Whether removal actually took place. Safe to ignore </returns>
    bool RemoveService(IServiceBase service);

    /// <summary> Create a publisher for this node for a given topic, qos and message type </summary>
    /// <description> Can only be called in an initialized Ros2cs state. </description>
    /// <param name="topic"> Topic for the publisher. Naming restrictions of ros2 apply and violation results in an exception </param>
    /// <param name="qos"> Quality of Service settings. Not passing this parameter will result in default settings </param>
    /// <returns> Publisher for the topic, which can be used to publish messages </returns>
    Publisher<T> CreatePublisher<T>(string topic, QualityOfServiceProfile qos = null) where T : Message, new();

    /// <summary> Create a subscription for this node for a given topic, callback, qos and message type </summary>
    /// <description> Can only be called in an initialized Ros2cs state. </description>
    /// <param name="topic"> Topic to subscribe to. Naming restrictions of ros2 apply and violation results in an exception </param>
    /// <param name="callback"> Action to be called when message is received (through Spin or SpinOnce). The message passed to this callback is owned and disposed by ros2cs. Provide a lambda or a method </param>
    /// <param name="qos"> Quality of Service settings. Not passing this parameter will result in default settings </param>
    /// <returns> Subscription for the topic </returns>
    Subscription<T> CreateSubscription<T>(string topic, Action<T> callback, QualityOfServiceProfile qos = null) where T : Message, new();

    /// <summary> Remove a publisher </summary>
    /// <remarks> Removes the publisher from the node and calls Dispose on it. </remarks>
    /// <param name="publisher"> Publisher created with earlier CreatePublisher call </param>
    /// <returns> Whether removal actually took place. Safe to ignore </returns>
    bool RemovePublisher(IPublisherBase publisher);

    /// <summary> Remove a subscription </summary>
    /// <remarks> Removes the subscription from the node and calls Dispose on it. </remarks>
    /// <param name="subscription"> Subscription created with earlier CreateSubscription call </param>
    /// <returns> Whether removal actually took place. Safe to ignore </returns>
    bool RemoveSubscription(ISubscriptionBase subscription);
  }
}
