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
// - Added allocation and null-argument guards for QoS profile helpers.
// - Added liveliness QoS setter wrapper.
// - Added QoS duration setter wrappers and compile-time rmw enum checks.

#include <rmw/qos_profiles.h>
#include <rmw/types.h>
#include <rmw/rmw.h>
#include <rcl/rcl.h>
#include <stdint.h>
#include <stdlib.h>

#define RMW_STATIC_ASSERT(name, expr) typedef char name[(expr) ? 1 : -1]
RMW_STATIC_ASSERT(rmw_history_system_default_ordinal, RMW_QOS_POLICY_HISTORY_SYSTEM_DEFAULT == 0);
RMW_STATIC_ASSERT(rmw_history_keep_last_ordinal, RMW_QOS_POLICY_HISTORY_KEEP_LAST == 1);
RMW_STATIC_ASSERT(rmw_history_keep_all_ordinal, RMW_QOS_POLICY_HISTORY_KEEP_ALL == 2);
RMW_STATIC_ASSERT(rmw_reliability_system_default_ordinal, RMW_QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT == 0);
RMW_STATIC_ASSERT(rmw_reliability_reliable_ordinal, RMW_QOS_POLICY_RELIABILITY_RELIABLE == 1);
RMW_STATIC_ASSERT(rmw_reliability_best_effort_ordinal, RMW_QOS_POLICY_RELIABILITY_BEST_EFFORT == 2);
RMW_STATIC_ASSERT(rmw_durability_system_default_ordinal, RMW_QOS_POLICY_DURABILITY_SYSTEM_DEFAULT == 0);
RMW_STATIC_ASSERT(rmw_durability_transient_local_ordinal, RMW_QOS_POLICY_DURABILITY_TRANSIENT_LOCAL == 1);
RMW_STATIC_ASSERT(rmw_durability_volatile_ordinal, RMW_QOS_POLICY_DURABILITY_VOLATILE == 2);
RMW_STATIC_ASSERT(rmw_liveliness_system_default_ordinal, RMW_QOS_POLICY_LIVELINESS_SYSTEM_DEFAULT == 0);
RMW_STATIC_ASSERT(rmw_liveliness_automatic_ordinal, RMW_QOS_POLICY_LIVELINESS_AUTOMATIC == 1);
RMW_STATIC_ASSERT(rmw_liveliness_manual_by_topic_ordinal, RMW_QOS_POLICY_LIVELINESS_MANUAL_BY_TOPIC == 3);

static void set_rmw_time_from_nanoseconds(struct rmw_time_s * target, uint64_t nanoseconds)
{
  target->sec = nanoseconds / 1000000000ULL;
  target->nsec = nanoseconds % 1000000000ULL;
}

ROSIDL_GENERATOR_C_EXPORT
rmw_qos_profile_t * rmw_native_interface_create_qos_profile(int profile)
{
  // These local ordinals must match ROS2.QosPresetProfile in managed code.
  enum
  {
     SENSOR_DATA,
     PARAMETERS,
     DEFAULT,
     SERVICES_DEFAULT,
     PARAMETER_EVENTS,
     SYSTEM_DEFAULT
  };

  rmw_qos_profile_t * preset_profile = (rmw_qos_profile_t *)malloc(sizeof(rmw_qos_profile_t));
  if (preset_profile == NULL)
  {
    return NULL;
  }

  switch (profile)
  {
      case SENSOR_DATA: *preset_profile = rmw_qos_profile_sensor_data; break;
      case PARAMETERS: *preset_profile = rmw_qos_profile_parameters; break;
      case DEFAULT: *preset_profile = rmw_qos_profile_default; break;
      case SERVICES_DEFAULT: *preset_profile = rmw_qos_profile_services_default; break;
      case PARAMETER_EVENTS: *preset_profile = rmw_qos_profile_parameter_events; break;
      case SYSTEM_DEFAULT: *preset_profile = rmw_qos_profile_system_default; break;
      default:
        free(preset_profile);
        return NULL;
  }

  return preset_profile;
}

ROSIDL_GENERATOR_C_EXPORT
const char* rmw_native_interface_get_implementation_identifier()
{
  // rmw owns this static string. Managed code must marshal it without freeing it.
  return rmw_get_implementation_identifier();
}

ROSIDL_GENERATOR_C_EXPORT
void rmw_native_interface_delete_qos_profile(rmw_qos_profile_t * profile)
{
  // Dispose can be called from failed managed construction paths; NULL is a no-op.
  if (profile == NULL)
  {
    return;
  }
  free(profile);
}

ROSIDL_GENERATOR_C_EXPORT
void rmw_native_interface_set_history(rmw_qos_profile_t * profile, int history_mode, int history_depth)
{
  if (profile == NULL)
  {
    return;
  }
  profile->history = history_mode;
  // Managed QoS validation rejects non-positive KeepLast depth before this size_t cast.
  profile->depth = (size_t)history_depth;
}

ROSIDL_GENERATOR_C_EXPORT
void rmw_native_interface_set_reliability(rmw_qos_profile_t * profile, int reliability_mode)
{
  if (profile == NULL)
  {
    return;
  }
  profile->reliability = reliability_mode;
}

ROSIDL_GENERATOR_C_EXPORT
void rmw_native_interface_set_durability(rmw_qos_profile_t * profile, int durability_mode)
{
  if (profile == NULL)
  {
    return;
  }
  profile->durability = durability_mode;
}

ROSIDL_GENERATOR_C_EXPORT
void rmw_native_interface_set_liveliness(rmw_qos_profile_t * profile, int liveliness_mode)
{
  if (profile == NULL)
  {
    return;
  }
  profile->liveliness = liveliness_mode;
}

ROSIDL_GENERATOR_C_EXPORT
void rmw_native_interface_set_deadline(rmw_qos_profile_t * profile, uint64_t nanoseconds)
{
  if (profile == NULL)
  {
    return;
  }
  set_rmw_time_from_nanoseconds(&profile->deadline, nanoseconds);
}

ROSIDL_GENERATOR_C_EXPORT
void rmw_native_interface_set_lifespan(rmw_qos_profile_t * profile, uint64_t nanoseconds)
{
  if (profile == NULL)
  {
    return;
  }
  set_rmw_time_from_nanoseconds(&profile->lifespan, nanoseconds);
}

ROSIDL_GENERATOR_C_EXPORT
void rmw_native_interface_set_liveliness_lease_duration(
  rmw_qos_profile_t * profile,
  uint64_t nanoseconds)
{
  if (profile == NULL)
  {
    return;
  }
  set_rmw_time_from_nanoseconds(&profile->liveliness_lease_duration, nanoseconds);
}
