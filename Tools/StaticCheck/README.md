# Offline static checks

These scripts run without Unity or a GPU. They are the gate between stages when no
Editor is available, and they can run in CI.

| Script | What it checks |
|---|---|
| `static_check.py` | `.meta` completeness and GUID uniqueness, kernel-name literals vs `#pragma kernel`, `[ResourcePath]` targets, LightMode tags vs `InfinityPassIDs`, Markdown links, C#/HLSL GPU record layouts (`// GPU-RECORD:` markers), Shader Graph string contracts |
| `shader_check.py` | Compiles every compute kernel and every vertex/fragment entry with DXC (base variant plus one variant per keyword). Needs a DXC binary and the CoreRP package sources. |
| `new_meta.py` | Writes missing `.meta` files with fresh GUIDs. Never overwrites. |

DXC acceptance is necessary but not sufficient: Unity compiles Direct3D with FXC and
Metal through HLSLcc/DXC, so a clean DXC run does not prove every platform compiles.
Always confirm in the Editor.

```
python3 Tools/StaticCheck/static_check.py --repo .
python3 Tools/StaticCheck/shader_check.py --repo . --dxc <dxc> --corerp <path to com.unity.render-pipelines.core> --out shader-report.json
```
