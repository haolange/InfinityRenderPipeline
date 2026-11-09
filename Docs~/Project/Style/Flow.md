# Flow

A section declares the locals it uses before the statements that use them. Group only locals whose initializers are already valid. Do not move an initializer above the statement it reads, and do not change definite assignment.

A hot path is a struct, a Burst job, graph recording, or execution after warmup. Layout work on a hot path adds no type, no method, and no allocation. A `#region` is removed before compilation and is not that cost.

Do not change control flow, early returns, or call order to make a page scan. One statement stays one statement when it is split across lines only because the branch body moves to the next line.

```csharp
// Compact

int count = source.Length;
int write = 0;

for (int read = 0; read < count; ++read)
{
    if (source[read] < 0)
    {
        continue;
    }

    packed[write] = source[read];
    write += 1;
}
```
