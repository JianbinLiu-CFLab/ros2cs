// Copyright 2019-2021 Robotec.ai
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

using System.Runtime.CompilerServices;

// ros2cs_core consumes internal native-message helpers without making them public API.
[assembly:InternalsVisibleTo("ros2cs_core")]
// ros2cs_tests verifies internal loader and message contracts without widening production visibility.
[assembly:InternalsVisibleTo("ros2cs_tests")]
