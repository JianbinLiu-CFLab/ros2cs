This directory contains a patched Windows `rcpputils` runtime used by the
historical Foxy standalone path.

The patch targets `ros2/rcpputils` `src/find_library.cpp` from the Foxy-era
dependency set. On Windows, `find_library_path()` can fail to resolve DDS/RMW
implementation libraries when it returns an empty string after checking the
explicit search paths. Returning the bare DLL filename instead lets the Windows
loader continue through the normal `PATH` search order, which is the behavior
needed by the bundled standalone layout.

Later maintained distro lines should use their upstream `rcpputils` packages
instead of this patched Foxy resource unless a fresh validation run proves the
same fallback is still required.

`rcpputils.diff` intentionally contains only the fallback return-value change;
debug print statements are not part of the production patch.
