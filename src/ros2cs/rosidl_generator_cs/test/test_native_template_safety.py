# Copyright 2019-2021 Robotec.ai
# Modifications Copyright (c) 2026 Jianbin Liu.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#    http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

"""Regression tests for generated native/C# template safety contracts."""

import unittest
from pathlib import Path


RESOURCE_DIR = Path(__file__).parents[1] / "resource"
GENERATOR_DIR = Path(__file__).parents[1]


def read_template(name):
    return (RESOURCE_DIR / name).read_text(encoding="utf-8")


def read_generator_file(*parts):
    return (GENERATOR_DIR / Path(*parts)).read_text(encoding="utf-8")


class NativeTemplateSafetyTest(unittest.TestCase):
    """Lock down template comments and code patterns that protect ABI/lifetime safety."""

    def test_native_c_templates_include_bool_and_int_limits(self):
        """Native templates must include headers needed by bool and INT_MAX guards."""
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("#include <stdbool.h>", template)
                self.assertIn("#include <limits.h>", template)

    def test_service_native_templates_emit_preamble_for_request_and_response(self):
        """Service request and response templates both need the native preamble."""
        for template_name in ("srv.c.em", "srv_typesupport.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertNotIn("@[if service_req == message.structure.namespaced_type.name ]@", template)
                self.assertIn("#include <rosidl_runtime_c/visibility_control.h>", template)

    def test_primitive_sequence_writes_reject_negative_size(self):
        """Primitive sequence writes must reject negative managed sizes."""
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("if (size < 0)", template)

    def test_bounded_sequences_emit_maximum_size_guards(self):
        """Bounded sequences must preserve IDL maximum-size checks."""
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("BoundedSequence", template)
                self.assertIn("member.type.maximum_size", template)
                self.assertIn("(size_t)size > @(member.type.maximum_size)", template)

    def test_zero_size_sequence_writes_skip_memcpy(self):
        """Zero-sized writes must avoid memcpy with null sequence storage."""
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("if (size == 0)", template)
                self.assertIn("return true;", template)

    def test_sequence_reinit_finis_existing_capacity(self):
        """Message reuse must fini old sequence storage before reinitialization."""
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("previous_sequence_capacity", template)
                self.assertIn("size_changed && previous_sequence_capacity != 0", template)

    def test_indexed_sequence_accessors_reject_out_of_bounds_indices(self):
        """Indexed array/sequence accessors must reject negative and overflow indices."""
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("index < 0", template)
                self.assertIn("(size_t)index >= ros_message->@(member.name).size", template)

    def test_sequence_size_read_templates_guard_int_overflow(self):
        """Native size_t sequence sizes must not overflow managed int results silently."""
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("ros_message->@(member.name).size > INT_MAX", template)
                self.assertIn("*size = -1", template)
                self.assertIn("return -1", template)
                self.assertIn("(int)ros_message->@(member.name).size", template)

    def test_generated_csharp_templates_reject_native_size_overflow(self):
        """Generated C# templates must reject negative sentinel sizes from native code."""
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("if (arraySize < 0)", template)
                self.assertIn("if (__native_array_size < 0)", template)
                self.assertIn("size exceeds supported Int32 range", template)

    def test_string_read_templates_document_borrowed_pointer_lifetime(self):
        """String read helpers must document borrowed pointer lifetime."""
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("Returned string pointers are borrowed", template)

    def test_string_sequence_writes_document_required_init_call(self):
        """String sequence element writes must document the required init step."""
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("Sequence element writes require", template)
                self.assertIn("_native_init_sequence_@(member.name) to run first", template)

    def test_native_destroy_templates_accept_null(self):
        """Native destroy wrappers must accept null handles safely."""
        for template_name in ("msg_typesupport.c.em", "srv_typesupport.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("if (!raw_ros_message)", template)

    def test_type_support_templates_do_not_cache_unsynchronized_singletons(self):
        """Type-support wrappers must avoid unsynchronized static singleton caches."""
        for template_name in ("msg_typesupport.c.em", "srv_typesupport.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertNotIn("static const void * ts", template)
                self.assertNotIn("if (!ts)", template)
                self.assertIn("return (void *)ROSIDL_GET_", template)

    def test_service_type_support_template_documents_service_level_handle(self):
        """Service request/response wrappers must document service-level type support."""
        template = read_template("srv_typesupport.c.em")
        self.assertIn("service-level type support", template)

    def test_native_symbol_names_match_csharp_proc_address_templates(self):
        """Native symbol names must stay aligned with generated C# GetProcAddress calls."""
        self.assertIn("@(msg_typename)_native_get_type_support", read_template("msg_typesupport.c.em"))
        self.assertIn("@(c_full_name)_native_get_type_support", read_template("msg.cs.em"))
        self.assertIn("@(msg_typename)_native_get_type_support", read_template("srv_typesupport.c.em"))
        self.assertIn("@(c_full_name)_native_get_type_support", read_template("srv.cs.em"))

    def test_generated_csharp_templates_wrap_required_symbol_lookup(self):
        """Generated C# templates must diagnose missing ros2cs overlay symbols clearly."""
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("GetRequiredProcAddress", template)
                self.assertIn("is missing required symbol", template)
                self.assertIn("plain ROS typesupport library", template)

    def test_generated_csharp_templates_guard_disposed_type_support(self):
        """TypeSupportHandle must reject access after managed disposal."""
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("public IntPtr TypeSupportHandle", template)
                self.assertIn("throw new ObjectDisposedException(nameof(@(message_class)))", template)

    def test_generated_csharp_templates_track_owned_sequence_elements(self):
        """Generated C# must track sequence elements it allocated from native reads."""
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("ownedSequenceElements", template)
                self.assertIn("DisposeOwnedSequenceElements", template)
                self.assertIn("DisposeAllOwnedSequenceElements", template)
                self.assertIn("caller-supplied sequence", template)

    def test_generated_csharp_templates_coalesce_null_strings(self):
        """Generated string reads must convert null native string pointers to empty strings."""
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("Marshal.PtrToStringAnsi(pStr) ?? \"\"", template)
                self.assertIn("Marshal.PtrToStringUni(pStr) ?? \"\"", template)
                self.assertIn("Marshal.PtrToStringAnsi(native_read_field_@(member.name)(i, handle)) ?? \"\"", template)
                self.assertIn("Marshal.PtrToStringUni(native_read_field_@(member.name)(i, handle)) ?? \"\"", template)

    def test_generated_csharp_templates_guard_fixed_array_size_and_pointcloud_overflow(self):
        """Generated fixed-array and PointCloud2 paths must keep size guards."""
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("but IDL fixed array size is @(member.type.size)", template)
                self.assertIn("ulong point_cloud_size", template)
                self.assertIn("point_cloud_size > int.MaxValue", template)
                self.assertIn("point_cloud_size > (ulong)Data.Length", template)

    def test_generated_csharp_templates_log_unsupported_preload_rmw(self):
        """Unsupported RMW preload cases must be logged instead of silently ignored."""
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("No generated", template)
                self.assertIn("preload rule for RMW_IMPLEMENTATION", template)

    def test_csharp_native_load_strings_document_cmake_native_suffix(self):
        """Generated C# and CMake must agree on the _native runtime library suffix."""
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("LoadLibrary appends", template)
                self.assertIn("\"_native\" infix used by CMake OUTPUT_NAME", template)

        cmake = read_generator_file("cmake", "rosidl_generator_cs_generate_interfaces.cmake")
        self.assertIn('OUTPUT_NAME                             "${_runtime_name}_native"', cmake)

    def test_templates_carry_upstream_and_fork_attribution(self):
        """All templates must preserve upstream-derived and fork-modification attribution."""
        for template_name in (
            "idl.c.em",
            "idl.cs.em",
            "idl_typesupport.c.em",
            "msg.c.em",
            "msg.cs.em",
            "msg_typesupport.c.em",
            "srv.c.em",
            "srv.cs.em",
            "srv_typesupport.c.em",
        ):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("Derived from RobotecAI rosidl_generator_cs templates", template)
                self.assertIn("Modifications Copyright (c) 2026 Jianbin Liu", template)

    def test_typesupport_templates_document_native_message_ownership(self):
        """Create/destroy/type-support wrappers must document ownership boundaries."""
        for template_name in ("msg_typesupport.c.em", "srv_typesupport.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("Caller owns the returned", template)
                self.assertIn("must release it with", template)
                self.assertIn("Managed callers borrow this handle and must not free it", template)
                self.assertIn("__destroy function recursively releases", template)

    def test_native_nested_handle_accessors_document_borrowed_lifetime(self):
        """Nested message handle accessors must state that returned pointers are borrowed."""
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("Returned nested message pointers are borrowed", template)

    def test_generated_csharp_handle_overloads_document_borrowed_handles(self):
        """Handle-taking read/write overloads must not imply native handle ownership transfer."""
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("Borrow an externally-owned native message handle", template)
                self.assertIn("caller retains ownership of the handle", template)

    def test_generated_csharp_templates_name_mono_platform_values(self):
        """Mono platform detection must use named constants instead of bare numeric values."""
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("monoPlatformUnix", template)
                self.assertIn("monoPlatformMacOs", template)
                self.assertIn("monoPlatformOldLinux", template)

    def test_resource_templates_do_not_keep_stale_todos(self):
        """Template comments should describe current contracts rather than stale TODOs."""
        for template_path in RESOURCE_DIR.glob("*.em"):
            with self.subTest(template=template_path.name):
                self.assertNotIn("TODO", template_path.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
