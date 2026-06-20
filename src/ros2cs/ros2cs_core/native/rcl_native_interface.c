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
// - Added allocation failure and null dispose guards for clock wrappers.
// - Finalizes rcl clock before freeing the wrapper allocation.
// - Added allocation and null-argument guards for rcl option wrapper helpers.
// - Finalizes rcl init options when rcl_init fails.
// - Finalizes context if init-options cleanup fails after rcl_init succeeds.
// - Finalizes node options before freeing their wrapper allocation.
// - Surfaced option finalization return codes and guarded null option disposals.
// - Added node option setter wrappers to avoid managed rcl_node_options_t layout coupling.

#include <rcl/error_handling.h>
#include <rcl/graph.h>
#include <rcl/node.h>
#include <rcl/rcl.h>
#include <rcl/time.h>
#include <rcutils/allocator.h>
#include <rcutils/types.h>
#include <rmw/qos_profiles.h>
#include <rmw/types.h>
#include <stdbool.h>
#include <stdlib.h>
#include <string.h>

typedef struct rclcs_string_array_s
{
  char ** data;
  size_t size;
} rclcs_string_array_t;

typedef struct rclcs_topic_names_and_types_s
{
  char ** names;
  rclcs_string_array_t * types;
  size_t size;
} rclcs_topic_names_and_types_t;

ROSIDL_GENERATOR_C_EXPORT
int rclcs_init(rcl_context_t *context, rcl_allocator_t allocator)
{
  rcl_init_options_t init_options = rcl_get_zero_initialized_init_options();
  rcl_ret_t ret = rcl_init_options_init(&init_options, allocator);
  if (ret != RCL_RET_OK)
  {
    return (int)ret;
  }

  ret = rcl_init(0, NULL, &init_options, context);
  if (ret != RCL_RET_OK)
  {
    rcl_init_options_fini(&init_options);
    return (int)ret;
  }

  ret = rcl_init_options_fini(&init_options);
  if (ret != RCL_RET_OK)
  {
    rcl_shutdown(context);
    rcl_context_fini(context);
  }
  return ret;
}

ROSIDL_GENERATOR_C_EXPORT
size_t rclcs_sizeof_rcl_node_t()
{
  return sizeof(rcl_node_t);
}

ROSIDL_GENERATOR_C_EXPORT
size_t rclcs_sizeof_rcl_context_t()
{
  return sizeof(rcl_context_t);
}

ROSIDL_GENERATOR_C_EXPORT
size_t rclcs_sizeof_rcl_wait_set_t()
{
  return sizeof(rcl_wait_set_t);
}

ROSIDL_GENERATOR_C_EXPORT
size_t rclcs_sizeof_rcl_rmw_request_id_t()
{
  return sizeof(rmw_request_id_t);
}

ROSIDL_GENERATOR_C_EXPORT
rcl_node_options_t * rclcs_node_create_default_options()
{
  rcl_node_options_t  * default_node_options_handle = (rcl_node_options_t *)malloc(sizeof(rcl_node_options_t));
  if (default_node_options_handle == NULL)
  {
    return NULL;
  }
  *default_node_options_handle = rcl_node_get_default_options();
  return default_node_options_handle;
}

ROSIDL_GENERATOR_C_EXPORT
int rclcs_node_options_set_enable_rosout(rcl_node_options_t * node_options_handle, bool enable_rosout)
{
  if (node_options_handle == NULL)
  {
    return RCL_RET_INVALID_ARGUMENT;
  }
  node_options_handle->enable_rosout = enable_rosout;
  return RCL_RET_OK;
}

ROSIDL_GENERATOR_C_EXPORT
int rclcs_node_dispose_options(rcl_node_options_t * node_options_handle)
{
  if (node_options_handle == NULL)
  {
    return RCL_RET_OK;
  }
  rcl_ret_t ret = rcl_node_options_fini(node_options_handle);
  free(node_options_handle);
  return (int)ret;
}

ROSIDL_GENERATOR_C_EXPORT
rcl_subscription_options_t *rclcs_subscription_create_options(rmw_qos_profile_t * qos)
{
  rcl_subscription_options_t  * default_subscription_options_handle = (rcl_subscription_options_t *)malloc(sizeof(rcl_subscription_options_t));
  if (default_subscription_options_handle == NULL)
  {
    return NULL;
  }
  if (qos == NULL)
  {
    free(default_subscription_options_handle);
    return NULL;
  }
  *default_subscription_options_handle = rcl_subscription_get_default_options();
  default_subscription_options_handle->qos = *qos;
  return default_subscription_options_handle;
}

ROSIDL_GENERATOR_C_EXPORT
int rclcs_subscription_dispose_options(rcl_subscription_options_t *subscription_options_handle)
{
  if (subscription_options_handle == NULL)
  {
    return RCL_RET_OK;
  }
  rcl_ret_t ret = rcl_subscription_options_fini(subscription_options_handle);
  free(subscription_options_handle);
  return (int)ret;
}

ROSIDL_GENERATOR_C_EXPORT
rcl_publisher_options_t *rclcs_publisher_create_options(rmw_qos_profile_t * qos)
{
  rcl_publisher_options_t *default_publisher_options_handle = (rcl_publisher_options_t *)malloc(sizeof(rcl_publisher_options_t));
  if (default_publisher_options_handle == NULL)
  {
    return NULL;
  }
  if (qos == NULL)
  {
    free(default_publisher_options_handle);
    return NULL;
  }
  *default_publisher_options_handle = rcl_publisher_get_default_options();
  default_publisher_options_handle->qos = *qos;
  return default_publisher_options_handle;
}

ROSIDL_GENERATOR_C_EXPORT
void rclcs_publisher_dispose_options(rcl_publisher_options_t * publisher_options_handle)
{
  // Jazzy exposes no rcl_publisher_options_fini; defaults are plain values today.
  if (publisher_options_handle == NULL)
  {
    return;
  }
  free(publisher_options_handle);
}

ROSIDL_GENERATOR_C_EXPORT
rcl_client_options_t *rclcs_client_create_options(rmw_qos_profile_t * qos)
{
  rcl_client_options_t *default_client_options_handle = (rcl_client_options_t *)malloc(sizeof(rcl_client_options_t));
  if (default_client_options_handle == NULL)
  {
    return NULL;
  }
  if (qos == NULL)
  {
    free(default_client_options_handle);
    return NULL;
  }
  *default_client_options_handle = rcl_client_get_default_options();
  default_client_options_handle->qos = *qos;
  return default_client_options_handle;
}

ROSIDL_GENERATOR_C_EXPORT
void rclcs_client_dispose_options(rcl_client_options_t * client_options_handle)
{
  // Jazzy exposes no rcl_client_options_fini; defaults are plain values today.
  if (client_options_handle == NULL)
  {
    return;
  }
  free(client_options_handle);
}

ROSIDL_GENERATOR_C_EXPORT
rcl_service_options_t *rclcs_service_create_options(rmw_qos_profile_t * qos)
{
  rcl_service_options_t *default_service_options_handle = (rcl_service_options_t *)malloc(sizeof(rcl_service_options_t));
  if (default_service_options_handle == NULL)
  {
    return NULL;
  }
  if (qos == NULL)
  {
    free(default_service_options_handle);
    return NULL;
  }
  *default_service_options_handle = rcl_service_get_default_options();
  default_service_options_handle->qos = *qos;
  return default_service_options_handle;
}

ROSIDL_GENERATOR_C_EXPORT
void rclcs_service_dispose_options(rcl_service_options_t * service_options_handle)
{
  // Jazzy exposes no rcl_service_options_fini; defaults are plain values today.
  if (service_options_handle == NULL)
  {
    return;
  }
  free(service_options_handle);
}

ROSIDL_GENERATOR_C_EXPORT
char * rclcs_get_error_string()
{
  rcl_error_string_t error_string = rcl_get_error_string();
  char * error_c_string = strdup(error_string.str);
  return error_c_string;
}

ROSIDL_GENERATOR_C_EXPORT
void rclcs_dispose_error_string(char * error_c_string)
{
  free(error_c_string);
}

ROSIDL_GENERATOR_C_EXPORT
void rclcs_dispose_topic_names_and_types(rclcs_topic_names_and_types_t * result)
{
  if (result == NULL)
  {
    return;
  }

  for (size_t i = 0; i < result->size; i++)
  {
    if (result->names != NULL)
    {
      free(result->names[i]);
    }
    if (result->types != NULL)
    {
      for (size_t j = 0; j < result->types[i].size; j++)
      {
        free(result->types[i].data[j]);
      }
      free(result->types[i].data);
    }
  }

  free(result->names);
  free(result->types);
  free(result);
}

ROSIDL_GENERATOR_C_EXPORT
int rclcs_get_topic_names_and_types(
  const rcl_node_t * node,
  bool no_demangle,
  rclcs_topic_names_and_types_t ** result)
{
  if (result == NULL)
  {
    return RCL_RET_INVALID_ARGUMENT;
  }
  *result = NULL;
  if (node == NULL)
  {
    return RCL_RET_INVALID_ARGUMENT;
  }

  rcl_allocator_t allocator = rcl_get_default_allocator();
  rcl_names_and_types_t names_and_types = rcl_get_zero_initialized_names_and_types();
  rcl_ret_t ret = rcl_get_topic_names_and_types(
    node,
    &allocator,
    no_demangle,
    &names_and_types);
  if (ret != RCL_RET_OK)
  {
    return (int)ret;
  }

  rclcs_topic_names_and_types_t * flattened =
    (rclcs_topic_names_and_types_t *)calloc(1, sizeof(rclcs_topic_names_and_types_t));
  if (flattened == NULL)
  {
    rcl_names_and_types_fini(&names_and_types);
    return RCL_RET_BAD_ALLOC;
  }

  flattened->size = names_and_types.names.size;
  if (flattened->size > 0)
  {
    flattened->names = (char **)calloc(flattened->size, sizeof(char *));
    flattened->types =
      (rclcs_string_array_t *)calloc(flattened->size, sizeof(rclcs_string_array_t));
    if (flattened->names == NULL || flattened->types == NULL)
    {
      rclcs_dispose_topic_names_and_types(flattened);
      rcl_names_and_types_fini(&names_and_types);
      return RCL_RET_BAD_ALLOC;
    }
  }

  for (size_t i = 0; i < flattened->size; i++)
  {
    flattened->names[i] = strdup(names_and_types.names.data[i]);
    if (flattened->names[i] == NULL)
    {
      rclcs_dispose_topic_names_and_types(flattened);
      rcl_names_and_types_fini(&names_and_types);
      return RCL_RET_BAD_ALLOC;
    }

    size_t type_count = names_and_types.types[i].size;
    flattened->types[i].size = type_count;
    if (type_count == 0)
    {
      continue;
    }

    flattened->types[i].data = (char **)calloc(type_count, sizeof(char *));
    if (flattened->types[i].data == NULL)
    {
      rclcs_dispose_topic_names_and_types(flattened);
      rcl_names_and_types_fini(&names_and_types);
      return RCL_RET_BAD_ALLOC;
    }

    for (size_t j = 0; j < type_count; j++)
    {
      flattened->types[i].data[j] = strdup(names_and_types.types[i].data[j]);
      if (flattened->types[i].data[j] == NULL)
      {
        rclcs_dispose_topic_names_and_types(flattened);
        rcl_names_and_types_fini(&names_and_types);
        return RCL_RET_BAD_ALLOC;
      }
    }
  }

  rcl_names_and_types_fini(&names_and_types);
  *result = flattened;
  return RCL_RET_OK;
}

ROSIDL_GENERATOR_C_EXPORT
rcl_clock_t * rclcs_ros_clock_create(rcl_allocator_t * allocator_handle)
{
  rcl_clock_t  * clock_handle = (rcl_clock_t *)malloc(sizeof(rcl_clock_t));
  // Return NULL on allocation failure so managed code can throw a clear creation error.
  if (clock_handle == NULL)
  {
    return NULL;
  }
  int32_t ret = rcl_ros_clock_init(clock_handle, allocator_handle);
  if (ret != RCL_RET_OK)
  {
    free(clock_handle);
    return NULL;
  }
  return clock_handle;
}

ROSIDL_GENERATOR_C_EXPORT
void rclcs_ros_clock_dispose(rcl_clock_t * clock_handle)
{
  // Dispose can be called from finalizers or failed construction paths; NULL is a no-op.
  if (clock_handle == NULL)
  {
    return;
  }
  // Finalize the rcl clock before freeing the wrapper allocation.
  rcl_clock_fini(clock_handle);
  free(clock_handle);
}
