// Copyright 2019-2021 Robotec.ai
// Copyright 2019 Dyno Robotics (by Samuel Lindgren samuel@dynorobotics.se)
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Audited exception definitions for Jazzy lifecycle/error-path hardening.
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
using System.Runtime.Serialization;

namespace ROS2
{
    /// <summary>Base exception for native library load failures. Prefer catching this type.</summary>
    [Serializable]
    public class UnsatisfiedLinkException : Exception {
      public UnsatisfiedLinkException () : base() { }
      public UnsatisfiedLinkException (string message) : base (message) { }
      public UnsatisfiedLinkException (string message, System.Exception inner) : base (message, inner) { }
      protected UnsatisfiedLinkException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Base exception for unsupported or unrecognized runtime platforms.
    /// Prefer catching this type over the concrete <see cref="UnknownPlatformError"/>.
    /// </summary>
    [Serializable]
    public class UnknownPlatformException : Exception {
      public UnknownPlatformException () : base() { }
      public UnknownPlatformException (string message) : base (message) { }
      public UnknownPlatformException (string message, System.Exception inner) : base (message, inner) { }
      protected UnknownPlatformException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Concrete native library load failure thrown by current loader implementations.
    /// Catch <see cref="UnsatisfiedLinkException"/> to handle all native link failures.
    /// </summary>
    [Serializable]
    public class UnsatisfiedLinkError : UnsatisfiedLinkException {
      public UnsatisfiedLinkError () : base() { }
      public UnsatisfiedLinkError (string message) : base (message) { }
      public UnsatisfiedLinkError (string message, System.Exception inner) : base (message, inner) { }
      protected UnsatisfiedLinkError(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Concrete unsupported-platform error thrown by the loader factory.
    /// Catch <see cref="UnknownPlatformException"/> to handle all platform detection failures.
    /// </summary>
    [Serializable]
    public class UnknownPlatformError : UnknownPlatformException {
      public UnknownPlatformError () : base() { }
      public UnknownPlatformError (string message) : base (message) { }
      public UnknownPlatformError (string message, System.Exception inner) : base (message, inner) { }
      protected UnknownPlatformError(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Thrown when a native ROS 2 call returns a non-success return code.
    /// Distinct from <see cref="UnsatisfiedLinkException"/>, which signals a native library loader failure.
    /// </summary>
    [Serializable]
    public class RuntimeError : Exception
    {
      /// <summary>
      /// The rcl/rmw return code, or <c>null</c> when the error originates from a managed-only path.
      /// </summary>
      public int? ReturnCode { get; private set; }

      public RuntimeError() : base() {}
      public RuntimeError(string message) : base(message) {}
      public RuntimeError(string message, Exception inner) : base(message, inner) {}
      public RuntimeError(string message, int returnCode) : base(message) { ReturnCode = returnCode; }
      public RuntimeError(string message, int returnCode, Exception inner) : base(message, inner) { ReturnCode = returnCode; }
      protected RuntimeError(SerializationInfo info, StreamingContext context) : base(info, context)
      {
        ReturnCode = (int?)info.GetValue(nameof(ReturnCode), typeof(int?));
      }

      public override void GetObjectData(SerializationInfo info, StreamingContext context)
      {
        base.GetObjectData(info, context);
        info.AddValue(nameof(ReturnCode), ReturnCode, typeof(int?));
      }
    }

    /// <summary>Thrown when ROS 2 operations are attempted before <c>Ros2cs.Init()</c> succeeds.</summary>
    [Serializable]
    public class NotInitializedException : InvalidOperationException
    {
      public NotInitializedException() : base() {}
      public NotInitializedException(string message) : base(message) {}
      public NotInitializedException(string message, Exception inner) : base(message, inner) {}
      protected NotInitializedException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>Thrown when a node name fails ROS 2 node-name validation.</summary>
    [Serializable]
    public class InvalidNodeNameException : ArgumentException
    {
      public InvalidNodeNameException() : base() {}
      public InvalidNodeNameException(string message) : base(message) {}
      public InvalidNodeNameException(string message, Exception inner) : base(message, inner) {}
      protected InvalidNodeNameException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>Thrown when a node namespace fails ROS 2 namespace validation.</summary>
    [Serializable]
    public class InvalidNamespaceException : ArgumentException
    {
      public InvalidNamespaceException() : base() {}
      public InvalidNamespaceException(string message) : base(message) {}
      public InvalidNamespaceException(string message, Exception inner) : base(message, inner) {}
      protected InvalidNamespaceException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
    /// <summary>
    /// Exception thrown when trying to wait on an empty wait set.
    /// </summary>
    [Serializable]
    public class WaitSetEmptyException : InvalidOperationException
    {
      public WaitSetEmptyException() : base()
      { }

      /// <inheritdoc />
      public WaitSetEmptyException(string message) : base(message)
      { }

      /// <inheritdoc />
      public WaitSetEmptyException(string message, Exception innerException) : base(message, innerException)
      { }

      protected WaitSetEmptyException(SerializationInfo info, StreamingContext context) : base(info, context)
      { }
    }
}
