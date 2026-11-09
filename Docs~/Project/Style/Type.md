# Type

The comment at the start of a type is one short sentence about what the type holds or does. The contract lives in `ARCHITECTURE.md`. Do not copy the contract into the type.

Write every access modifier. A member that omitted one is `private`. Do not raise or lower an existing `public`, `internal`, or `protected` modifier. `private` added to a member that was already private does not change serialization.

Keep one primary type in a file unless a pass or a feature group is clearer together. Do not split a type out of its file for layout.

```csharp
// One camera's accepted jitter sample.
internal static class TemporalJitter
{
    private static int s_Index;
}
```
