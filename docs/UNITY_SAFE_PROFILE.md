# Unity-Safe Profile

ros2cs remains the broad generated-message backend used by ROS2 For Unity.

The Unity-safe profile is a compatibility boundary, not a replacement runtime.
It describes APIs and validation patterns that must remain consumable by Unity
and downstream R2FU packages.

## Preserved Guarantees

- Generated message namespaces and public contracts remain stable.
- `ros2cs_common` and `ros2cs_core` stay Unity-compatible.
- R2FU asset packaging remains the downstream runtime target.
- New APIs are additive unless a dedicated migration plan says otherwise.
- Default node, graph, generated message, and artifact behavior remain
  equivalent unless an explicit migration plan changes them.

## Avoid In Unity-Facing APIs

- `ValueTask`
- `IAsyncEnumerable`
- `IAsyncDisposable`
- `ManualResetValueTaskSource`
- net8-only runtime dependencies
- direct dependency on `Rcl.NET.dll` or `Rosidl.Runtime.dll`

## Borrowed Rcl.NET Lessons

- Keep Unity-facing surfaces small.
- Prefer explicit runtime artifact closure.
- Validate with Unity Player smoke, not build-only evidence.
- Add graph convenience APIs without changing existing message behavior.
- Keep lightweight runtime behavior opt-in.

## Validation Status

Unity Player smoke and external ROS 2 echo validation are the target evidence for
Unity-facing runtime claims. Build-only evidence is not enough for those claims.

This ros2cs maintenance branch can document APIs and profile boundaries on its
own, but it should only mark R2FU Player validation as complete when the paired
ROS2 For Unity runtime artifact evidence is recorded for the same ros2cs commit.

## Lightweight Node Options

`NodeOptions` defaults preserve the existing ros2cs node behavior. The existing
`Ros2cs.CreateNode(string nodeName)` overload continues to create nodes with the
default ROS 2 node options used by ros2cs before this profile work.

For minimal Unity runtime profiles where rosout is not needed, callers may opt
in explicitly:

```csharp
INode node = Ros2cs.CreateNode(
  "unity_lightweight_node",
  new NodeOptions { EnableRosout = false });
```

This is a ROS-visible tradeoff and should not be used as an implicit default for
R2FU or generated-message workflows.

## Not A Replacement Track

The Unity-safe profile does not replace ros2cs generated message support. It is
a guardrail for future changes so ros2cs can borrow proven Unity validation and
boundary ideas without losing its main value: broad generated ROS 2 message
coverage and R2FU compatibility.
