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

import argparse
import os
import subprocess
import sys


def _read_outputs(path):
    if not os.path.exists(path):
        raise FileNotFoundError(
            "Declared outputs file does not exist: {0}".format(path))

    with open(path, 'r', encoding='utf-8') as handle:
        return [line.strip() for line in handle if line.strip()]


def _remove_outputs(outputs):
    for output in outputs:
        try:
            os.remove(output)
        except FileNotFoundError:
            pass


def main(argv=None):
    parser = argparse.ArgumentParser(
        description='Run rosidl_generator_cs after clearing declared outputs.')
    parser.add_argument(
        '--outputs-file',
        required=True,
        help='Text file containing one declared generator output per line.')
    parser.add_argument(
        '--generator',
        required=True,
        help='Path to the rosidl_generator_cs entrypoint.')
    parser.add_argument(
        'generator_args',
        nargs=argparse.REMAINDER,
        help='Arguments passed through to the generator entrypoint.')
    args = parser.parse_args(argv)

    try:
        outputs = _read_outputs(args.outputs_file)
    except OSError as e:
        print("rosidl_generator_cs clean wrapper failed to read outputs file: {0}".format(e), file=sys.stderr)
        return 2

    _remove_outputs(outputs)

    generator_args = args.generator_args
    if generator_args and generator_args[0] == '--':
        generator_args = generator_args[1:]

    command = [sys.executable, args.generator] + generator_args
    try:
        result = subprocess.run(command)
    except OSError as e:
        _remove_outputs(outputs)
        print("rosidl_generator_cs clean wrapper failed to run generator: {0}".format(e), file=sys.stderr)
        return 2

    if result.returncode != 0:
        _remove_outputs(outputs)
    return result.returncode


if __name__ == '__main__':
    raise SystemExit(main())
