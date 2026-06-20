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
// - Added immutable managed graph-query result objects for topic names and type names.
// - Keeps native flattened graph data out of public API ownership.

using System.Collections.Generic;

namespace ROS2
{
  /// <summary>Topic name and its visible ROS type names from the local graph cache.</summary>
  public sealed class TopicNamesAndTypes
  {
    internal TopicNamesAndTypes(string name, IReadOnlyList<string> types)
    {
      Name = name;
      Types = types;
    }

    /// <summary>Topic name as reported by rcl.</summary>
    public string Name { get; }

    /// <summary>Type names associated with the topic.</summary>
    public IReadOnlyList<string> Types { get; }
  }
}
