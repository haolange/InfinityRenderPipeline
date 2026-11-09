# Page

A page is scannable as sections. A `#region` is one way to mark a long C# section. It does not replace the section itself.

Leave one blank line before a section. The first line of the section is a short label. In C# the label is a comment. The label names the section. It does not restate the contract.

Write one statement on a line. The branch of an `if`, an `else`, and a `switch` each occupies its own line. A condition stays with its `if` or `case`. The body starts on the next line.

```csharp
// Frustum

if (bounds.Intersects(frustum))
{
    visible[index] = true;
}
else
{
    visible[index] = false;
}
```

Use a `#region` when a C# section is long enough to read as a block. Runtime code and inspectors may both use it. Indent the body one level inside the region. Repeat the region name on `#endregion`.

```csharp
#region Frustum

if (bounds.Intersects(frustum))
{
    visible[index] = true;
}

#endregion Frustum
```

A region is removed before compilation. It is not a hot-path cost. Do not add a region to a section that is already short enough to see at once.
