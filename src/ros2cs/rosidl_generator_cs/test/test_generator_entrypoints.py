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

"""Regression tests for rosidl_generator_cs entrypoints and wrapper contracts.

The suite covers failures that tend to break incremental builds silently:
import errors hidden by package fallbacks, zero generated outputs, native runtime
name drift, and stale partial outputs left behind by failed generator processes.
"""

import importlib.util
import pathlib
import sys
import tempfile
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
        """The main mapping plus one call per typesupport implementation must run."""
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
        expected_calls = 1 + 2  # one idl.cs/idl.c mapping plus two typesupport entrypoints
        self.assertEqual(expected_calls, len(calls))

    def test_generate_cs_rejects_empty_generator_result(self):
        """A swallowed generator failure that writes no files must fail fast."""
        module = load_module(
            "generate_cs_impl_under_test_failure",
            PACKAGE_ROOT / "rosidl_generator_cs" / "generate_cs_impl.py")

        def fake_generate_files(generator_arguments_file, mapping, additional_context=None):
            return []

        module.generate_files = fake_generate_files

        with self.assertRaisesRegex(RuntimeError, "generated no files"):
            module.generate_cs("arguments.json", ["rosidl_typesupport_c"], "DotNetCore")

    def test_generate_cs_propagates_generator_exception(self):
        """Template expansion exceptions must remain visible to colcon/CMake."""
        module = load_module(
            "generate_cs_impl_under_test_exception",
            PACKAGE_ROOT / "rosidl_generator_cs" / "generate_cs_impl.py")

        def fake_generate_files(generator_arguments_file, mapping, additional_context=None):
            raise RuntimeError("template expansion failed")

        module.generate_files = fake_generate_files

        with self.assertRaisesRegex(RuntimeError, "template expansion failed"):
            module.generate_cs("arguments.json", ["rosidl_typesupport_c"], "DotNetCore")

    def test_package_init_does_not_swallow_generator_import_error(self):
        """Package fallback imports must not turn dependency import errors into AttributeError."""
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
        """The Windows fallback loader must avoid the deprecated load_module API."""
        bin_source = (PACKAGE_ROOT / "bin" / "rosidl_generator_cs").read_text()

        self.assertIn("spec_from_file_location", bin_source)
        self.assertIn("exec_module", bin_source)
        self.assertNotIn(".load_module(", bin_source)


class GeneratorCMakeContractTest(unittest.TestCase):
    def test_runtime_names_strip_ep_typesupport_suffix(self):
        """Generated CMake runtime DLL names must match generated C# LoadLibrary strings."""
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
    def test_clean_generate_reports_missing_outputs_file(self):
        """Wrapper infrastructure failures use the wrapper-reserved exit code."""
        module = load_module(
            "clean_generate_missing_outputs_under_test",
            PACKAGE_ROOT / "rosidl_generator_cs" / "clean_generate.py")

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = pathlib.Path(temp_dir)
            generator = temp_path / "generator.py"
            generator.write_text("raise SystemExit(0)\n", encoding="utf-8")

            result = module.main([
                "--outputs-file", str(temp_path / "missing_outputs.txt"),
                "--generator", str(generator),
            ])

            self.assertEqual(2, result)

    def test_clean_generate_keeps_successful_outputs(self):
        """Successful generator runs must leave declared outputs in place."""
        module = load_module(
            "clean_generate_success_under_test",
            PACKAGE_ROOT / "rosidl_generator_cs" / "clean_generate.py")

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = pathlib.Path(temp_dir)
            output = temp_path / "generated.cs"
            outputs_file = temp_path / "outputs.txt"
            outputs_file.write_text(str(output) + "\n", encoding="utf-8")
            generator = temp_path / "generator.py"
            generator.write_text(
                "import pathlib, sys\n"
                "pathlib.Path(sys.argv[1]).write_text('complete', encoding='utf-8')\n"
                "raise SystemExit(0)\n",
                encoding="utf-8")

            result = module.main([
                "--outputs-file", str(outputs_file),
                "--generator", str(generator),
                "--", str(output),
            ])

            self.assertEqual(0, result)
            self.assertEqual("complete", output.read_text(encoding="utf-8"))

    def test_clean_generate_deletes_declared_outputs_on_failure(self):
        """Failed generator runs must remove stale and partial declared outputs."""
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
