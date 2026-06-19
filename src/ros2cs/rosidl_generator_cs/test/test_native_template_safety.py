import unittest
from pathlib import Path


RESOURCE_DIR = Path(__file__).parents[1] / "resource"


def read_template(name):
    return (RESOURCE_DIR / name).read_text(encoding="utf-8")


class NativeTemplateSafetyTest(unittest.TestCase):
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

    def test_indexed_sequence_accessors_reject_out_of_bounds_indices(self):
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("index < 0", template)
                self.assertIn("(size_t)index >= ros_message->@(member.name).size", template)

    def test_string_read_templates_document_borrowed_pointer_lifetime(self):
        for template_name in ("msg.c.em", "srv.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("Returned string pointers are borrowed", template)

    def test_native_destroy_templates_accept_null(self):
        for template_name in ("msg_typesupport.c.em", "srv_typesupport.c.em"):
            with self.subTest(template=template_name):
                template = read_template(template_name)
                self.assertIn("if (!raw_ros_message)", template)

    def test_service_type_support_template_documents_service_level_handle(self):
        template = read_template("srv_typesupport.c.em")
        self.assertIn("service-level type support", template)

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


if __name__ == "__main__":
    unittest.main()
