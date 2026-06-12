import importlib.util
import pathlib
import sys
import tempfile
import textwrap
import unittest


PACKAGE_ROOT = pathlib.Path(__file__).resolve().parents[1]


def load_module(module_name, path, *, package=False):
    kwargs = {}
    if package:
        kwargs["submodule_search_locations"] = [str(path.parent)]
    spec = importlib.util.spec_from_file_location(module_name, path, **kwargs)
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    try:
        spec.loader.exec_module(module)
        return module
    finally:
        sys.modules.pop(module_name, None)


class GeneratorEntrypointTest(unittest.TestCase):
    def test_generate_cs_returns_zero_after_successful_generation(self):
        module = load_module(
            "generate_cs_impl_under_test",
            PACKAGE_ROOT / "rosidl_generator_cs" / "generate_cs_impl.py")
        calls = []

        def fake_generate_files(generator_arguments_file, mapping, additional_context=None):
            calls.append((generator_arguments_file, mapping, additional_context))
            return ["generated"]

        module.generate_files = fake_generate_files

        result = module.generate_cs("arguments.json", ["rosidl_typesupport_c"], "DotNetCore")

        self.assertEqual(0, result)
        self.assertEqual(2, len(calls))

    def test_package_init_does_not_swallow_generator_import_error(self):
        init_source = (PACKAGE_ROOT / "rosidl_generator_cs" / "__init__.py").read_text()

        with tempfile.TemporaryDirectory() as temp_dir:
            package_dir = pathlib.Path(temp_dir) / "probe_generator"
            package_dir.mkdir()
            (package_dir / "__init__.py").write_text(init_source)
            (package_dir / "generate_cs_impl.py").write_text(
                "raise ImportError('missing generator dependency')\n")

            with self.assertRaisesRegex(ImportError, "missing generator dependency"):
                load_module("probe_generator", package_dir / "__init__.py", package=True)

    def test_bin_fallback_uses_supported_importlib_loader_api(self):
        bin_source = (PACKAGE_ROOT / "bin" / "rosidl_generator_cs").read_text()

        self.assertIn("spec_from_file_location", bin_source)
        self.assertIn("exec_module", bin_source)
        self.assertNotIn(".load_module(", bin_source)


if __name__ == "__main__":
    unittest.main()
