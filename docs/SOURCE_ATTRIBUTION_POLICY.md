<!-- Copyright (c) 2026 Jianbin Liu. -->

# Source Attribution Policy

ros2cs may borrow design ideas from compatible open-source projects, but source
code copying and adaptation must be explicit.

## Categories

Design inspiration only: cite the source in the implementation report; no source
header change is required.

Small API or validation idea: cite the source in the implementation report; no
source header change is required.

Adapted code: add a file header naming the original project, source path, commit,
copyright holder, and license. Update `NOTICE`.

Direct copy: avoid unless necessary. If used, preserve the original copyright
and license notice and update `NOTICE`.

## Rcl.NET Status

The local rclnet fork is MIT licensed. Direct or adapted code from rclnet must
retain MIT attribution even if the surrounding ros2cs file uses Apache-2.0.

## Header Examples

For C# files with adapted rclnet code:

```csharp
// Copyright 2019-2021 Robotec.ai
// Copyright (c) 2022-2024 noelex
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Portions adapted from Rcl.NET.Unity/<source-file> at <commit>.
// The adapted portions are licensed under the MIT License.
// Other ros2cs portions are licensed under the Apache License, Version 2.0.
// See LICENSE.AL2 and NOTICE.
```

For C files with adapted rclnet code:

```c
// Copyright 2019-2021 Robotec.ai
// Copyright (c) 2022-2024 noelex
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Portions adapted from Rcl.NET.Unity/<source-file> at <commit>.
// The adapted portions are licensed under the MIT License.
// Other ros2cs portions are licensed under the Apache License, Version 2.0.
// See LICENSE.AL2 and NOTICE.
```

## Reports

Every rclnet-inspired implementation report should record one attribution
category:

```text
Attribution category: design inspiration only
Attribution category: small API/validation idea
Attribution category: adapted code
Attribution category: direct copy
```

If the category is `adapted code` or `direct copy`, the report must include the
source repository, source path, commit, license, and changed ros2cs notice/header
files.
