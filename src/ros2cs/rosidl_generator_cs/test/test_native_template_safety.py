import unittest
from pathlib import Path


RESOURCE_DIR = Path(__file__).parents[1] / "resource"
GENERATOR_DIR = Path(__file__).parents[1]


def read_template(name):
    return (RESOURCE_DIR / name).read_text(encoding="utf-8")


def read_generator_file(*parts):
    return (GENERATOR_DIR / Path(*parts)).read_text(encoding="utf-8")


class NativeTemplateSafetyTest(unittest.TestCase):
    def test_native_c_templates_include_bool_and_int_limits(self):
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("#include <stdbool.h>", template)
                self.assertIn("#include <limits.h>", template)

    def test_service_native_templates_emit_preamble_for_request_and_response(self):
        for template_name in ("srv.c.em", "srv_typesupport.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertNotIn("@[if service_req == message.structure.namespaced_type.name ]@", template)
                self.assertIn("#include <rosidl_runtime_c/visibility_control.h>", template)

    def test_primitive_sequence_writes_reject_negative_size(self):
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("if (size < 0)", template)

    def test_bounded_sequences_emit_maximum_size_guards(self):
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("BoundedSequence", template)
                self.assertIn("member.type.maximum_size", template)
                self.assertIn("(size_t)size > @(member.type.maximum_size)", template)

    def test_zero_size_sequence_writes_skip_memcpy(self):
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("if (size == 0)", template)
                self.assertIn("return true;", template)

    def test_sequence_reinit_finis_existing_capacity(self):
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("previous_sequence_capacity", template)
                self.assertIn("size_changed && previous_sequence_capacity != 0", template)

    def test_indexed_sequence_accessors_reject_out_of_bounds_indices(self):
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("index < 0", template)
                self.assertIn("(size_t)index >= ros_message->@(member.name).size", template)

    def test_sequence_size_read_templates_guard_int_overflow(self):
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("ros_message->@(member.name).size > INT_MAX", template)
                self.assertIn("*size = -1", template)
                self.assertIn("return -1", template)
                self.assertIn("(int)ros_message->@(member.name).size", template)

    def test_generated_csharp_templates_reject_native_size_overflow(self):
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("if (arraySize < 0)", template)
                self.assertIn("if (__native_array_size < 0)", template)
                self.assertIn("size exceeds supported Int32 range", template)

    def test_string_read_templates_document_borrowed_pointer_lifetime(self):
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("Returned string pointers are borrowed", template)

    def test_string_sequence_writes_document_required_init_call(self):
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("Sequence element writes require", template)
                self.assertIn("_native_init_sequence_@(member.name) to run first", template)

    def test_native_destroy_templates_accept_null(self):
        for template_name in ("msg_typesupport.c.em", "srv_typesupport.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("if (!raw_ros_message)", template)

    def test_type_support_templates_do_not_cache_unsynchronized_singletons(self):
        for template_name in ("msg_typesupport.c.em", "srv_typesupport.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertNotIn("static const void * ts", template)
                self.assertNotIn("if (!ts)", template)
                self.assertIn("return (void *)ROSIDL_GET_", template)

    def test_service_type_support_template_documents_service_level_handle(self):
        template = read_template("srv_typesupport.c.em")
        self.assertIn("service-level type support", template)

    def test_native_symbol_names_match_csharp_proc_address_templates(self):
        self.assertIn("@(msg_typename)_native_get_type_support", read_template("msg_typesupport.c.em"))
        self.assertIn("@(c_full_name)_native_get_type_support", read_template("msg.cs.em"))
        self.assertIn("@(msg_typename)_native_get_type_support", read_template("srv_typesupport.c.em"))
        self.assertIn("@(c_full_name)_native_get_type_support", read_template("srv.cs.em"))

    def test_generated_csharp_templates_wrap_required_symbol_lookup(self):
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("GetRequiredProcAddress", template)
                self.assertIn("is missing required symbol", template)
                self.assertIn("plain ROS typesupport library", template)

    def test_generated_csharp_templates_guard_disposed_type_support(self):
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("public IntPtr TypeSupportHandle", template)
                self.assertIn("throw new ObjectDisposedException(nameof(@(message_class)))", template)

    def test_generated_csharp_templates_track_owned_sequence_elements(self):
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("ownedSequenceElements", template)
                self.assertIn("DisposeOwnedSequenceElements", template)
                self.assertIn("DisposeAllOwnedSequenceElements", template)
                self.assertIn("caller-supplied sequence", template)

    def test_generated_csharp_templates_coalesce_null_strings(self):
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("Marshal.PtrToStringAnsi(pStr) ?? \"\"", template)
                self.assertIn("Marshal.PtrToStringUni(pStr) ?? \"\"", template)
                self.assertIn("Marshal.PtrToStringAnsi(native_read_field_@(member.name)(i, handle)) ?? \"\"", template)
                self.assertIn("Marshal.PtrToStringUni(native_read_field_@(member.name)(i, handle)) ?? \"\"", template)

    def test_generated_csharp_templates_guard_fixed_array_size_and_pointcloud_overflow(self):
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("but IDL fixed array size is @(member.type.size)", template)
                self.assertIn("ulong point_cloud_size", template)
                self.assertIn("point_cloud_size > int.MaxValue", template)
                self.assertIn("point_cloud_size > (ulong)Data.Length", template)

    def test_generated_csharp_templates_log_unsupported_preload_rmw(self):
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("No generated", template)
                self.assertIn("preload rule for RMW_IMPLEMENTATION", template)

    def test_csharp_native_load_strings_document_cmake_native_suffix(self):
        for template_name in ("msg.cs.em", "srv.cs.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("LoadLibrary appends", template)
                self.assertIn("\"_native\" infix used by CMake OUTPUT_NAME", template)

        cmake = read_generator_file("cmake", "rosidl_generator_cs_generate_interfaces.cmake")
        self.assertIn('OUTPUT_NAME                             "${_runtime_name}_native"', cmake)


if __name__ == "__main__":
    unittest.main()
