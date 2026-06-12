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


if __name__ == "__main__":
    unittest.main()
