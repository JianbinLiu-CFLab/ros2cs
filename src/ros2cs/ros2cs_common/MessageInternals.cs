// Copyright 2021 Robotec.ai
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
// - Added safe MessageInternals validation helper.
// - Cached message type support handles.

using System;
using System.Collections.Generic;

namespace ROS2
{
  namespace Internal
  { // TODO (adamdbrw) a namespace to warn where design did not deliver.

    /// <summary>Native message contract exposed by generated message assemblies.</summary>
    /// <remarks>
    /// This interface is public only because generated message assemblies, custom message assemblies,
    /// nested message implementations, and ros2cs_core must share one native-message contract. It is
    /// not an application-level API; external callers should use the public message, publisher,
    /// subscription, service, and client APIs instead of calling these members directly.
    /// </remarks>
    public interface MessageInternals
    {
      IntPtr Handle { get; }
      IntPtr TypeSupportHandle { get; }
      void ReadNativeMessage();
      void WriteNativeMessage();
    }

    /// <summary> An utility class to acquire type support for a given message type </summary>
    internal static class MessageTypeSupportHelper
    {
      // Type support handles are process-stable, so cache them to avoid repeated temporary messages.
      private static readonly object TypeSupportHandlesMutex = new object();
      private static readonly Dictionary<Type, IntPtr> TypeSupportHandles = new Dictionary<Type, IntPtr>();

      /// <summary>Validate that a generated message exposes the internal native-message contract.</summary>
      internal static MessageInternals AsMessageInternals(Message message, string argumentName)
      {
        if (message == null)
        {
          throw new ArgumentNullException(argumentName);
        }

        MessageInternals messageInternals = message as MessageInternals;
        if (messageInternals == null)
        {
          throw new InvalidOperationException(
            message.GetType().FullName + " must implement ROS2.Internal.MessageInternals");
        }

        return messageInternals;
      }

      internal static IntPtr GetTypeSupportHandle<T>() where T : Message, new()
      {
        Type messageType = typeof(T);
        IntPtr typeSupportHandle;
        lock (TypeSupportHandlesMutex)
        {
          if (TypeSupportHandles.TryGetValue(messageType, out typeSupportHandle))
          {
            return typeSupportHandle;
          }

          T msg = new T();
          try
          {
            // Create one temporary generated message only when the type support handle is not cached yet.
            typeSupportHandle = AsMessageInternals(msg, nameof(msg)).TypeSupportHandle;
          }
          finally
          {
            msg.Dispose();
          }

          TypeSupportHandles[messageType] = typeSupportHandle;
          return typeSupportHandle;
        }
      }
    }
  } // namespace Internal
} // namespace ROS2
