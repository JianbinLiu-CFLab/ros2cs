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

        result = module.generate_cs(
            "arguments.json",
            ["rosidl_typesupport_fastrtps_c", "rosidl_typesupport_introspection_c"],
            "DotNetCore")

        self.assertEqual(0, result)
        self.assertEqual(3, len(calls))

    def test_generate_cs_rejects_empty_generator_result(self):
        module = load_module(
            "generate_cs_impl_under_test_failure",
            PACKAGE_ROOT / "rosidl_generator_cs" / "generate_cs_impl.py")

        def fake_generate_files(generator_arguments_file, mapping, additional_context=None):
            return []

        module.generate_files = fake_generate_files

        with self.assertRaisesRegex(RuntimeError, "generated no files"):
            module.generate_cs("arguments.json", ["rosidl_typesupport_c"], "DotNetCore")

    def test_generate_cs_propagates_generator_exception(self):
        module = load_module(
            "generate_cs_impl_under_test_exception",
            PACKAGE_ROOT / "rosidl_generator_cs" / "generate_cs_impl.py")

        def fake_generate_files(generator_arguments_file, mapping, additional_context=None):
            raise RuntimeError("template expansion failed")

        module.generate_files = fake_generate_files

        with self.assertRaisesRegex(RuntimeError, "template expansion failed"):
            module.generate_cs("arguments.json", ["rosidl_typesupport_c"], "DotNetCore")

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


class GeneratorCMakeContractTest(unittest.TestCase):
    def test_runtime_names_strip_ep_typesupport_suffix(self):
        cmake_source = (
            PACKAGE_ROOT / "cmake" / "rosidl_generator_cs_generate_interfaces.cmake").read_text()

        self.assertIn('string(REGEX REPLACE "\\\\.ep\\\\..*$" "" _module_name', cmake_source)
        self.assertIn('set(_runtime_name "${_package_name}_${_module_name}__${_typesupport_impl}")', cmake_source)
        self.assertIn(
            'set(_runtime_name "${_package_name}_srv_${_module_name}__${_typesupport_impl}")',
            cmake_source)
        self.assertNotIn('${_base_msg_name}__${_typesupport_impl}', cmake_source)
        self.assertNotIn('${_base_srv_name}__${_typesupport_impl}', cmake_source)


class CleanGenerateWrapperTest(unittest.TestCase):
    def test_clean_generate_deletes_declared_outputs_on_failure(self):
        module = load_module(
            "clean_generate_under_test",
            PACKAGE_ROOT / "rosidl_generator_cs" / "clean_generate.py")

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = pathlib.Path(temp_dir)
            stale_output = temp_path / "stale.cs"
            stale_output.write_text("partial", encoding="utf-8")
            outputs_file = temp_path / "outputs.txt"
            outputs_file.write_text(str(stale_output) + "\n", encoding="utf-8")
            failing_generator = temp_path / "failing_generator.py"
            failing_generator.write_text(
                "import pathlib, sys\n"
                "pathlib.Path(sys.argv[1]).write_text('partial', encoding='utf-8')\n"
                "raise SystemExit(7)\n",
                encoding="utf-8")

            result = module.main([
                "--outputs-file", str(outputs_file),
                "--generator", str(failing_generator),
                "--", str(stale_output),
            ])

            self.assertEqual(7, result)
            self.assertFalse(stale_output.exists())


if __name__ == "__main__":
    unittest.main()
