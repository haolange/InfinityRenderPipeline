# Editor

Editor windows and inspectors use Unity IMGUI and `EditorStyles`, except the default Volume drawer on Global Settings, which is UI Toolkit hosting the Volume editor. Colors in the Render Graph Viewer are local to that window. The package does not ship a visual theme, type ramp or component library.

Assigning an Infinity pipeline asset selects the camera, light and Volume Profile inspectors below. Mesh, decal, atmosphere, diffusion and the pipeline asset inspectors are not gated on that assignment. InfinityLit follows the material shader.

## Windows

| Tool | Menu | Behavior |
|---|---|---|
| Render Pipeline Wizard | Window > Infinity > Render Pipeline Wizard | Checks color space, graphics API, pipeline assignment, Global Settings, the default Volume profile, the SRP Batcher, mixed lighting, the baking sky, light-shape sync, realtime-only tube and line lights, and convertible materials. Fix and Fix All are explicit. |
| Validate Default Volume Profile | Window > Infinity > Validate Default Volume Profile | Checks the Global Settings profile. A complete profile is left unchanged. An incomplete profile fails the check. |
| Append missing Volume components | Window > Infinity > Append Missing Default Volume Components | Adds absent default-profile components. Existing components, identities and values stay. |
| Material Conversion Wizard | Window > Infinity > Material Conversion Wizard | Converts checked Built-in, URP and HDRP materials in place. See [Materials](materials.md). |
| Frame Settings | Pipeline asset, and Camera > Custom Frame Settings | Decals, ambient occlusion, ray-traced ambient occlusion, contact shadows, screen-space reflection, screen-space indirect diffuse, volumetric fog, volumetric clouds, reflection probes, light cookies. The debugger shows the effective value and its source. |
| Light Explorer | Window > Rendering > Light Explorer | Adds Infinity Lights and Infinity Probes beside Unity's lights, probes, volumes and static emissives. Infinity light columns show the native type and the analytic shape, read-only. |
| Lighting Environment | Window > Rendering > Lighting > Environment | Realtime and baked environment, atmosphere profile, baking sky, ambient and reflection intensity. |
| Use Atmosphere As Baking Sky | Window > Infinity > Lighting > Use Atmosphere As Baking Sky | Writes the baking-sky assets for the saved scene. See [Baked lighting](baked-gi.md). |
| Rendering Debugger | Window > Analysis > Rendering Debugger | See [Rendering Debugger](rendering-debugger.md). |
| Render Graph Viewer | Window > Analysis > Infinity Render Graph Viewer | See below. |
| Validation fixtures | Window > Infinity > Create … Fixture | Scenes for lights, shadows, cookies, probes, baked lighting, screen space, decals, translucency, temporal effects, output, volumes and UI. |
| Image regression | Window > Infinity > Image Regression | Record Baselines and Run Comparison on fixture cameras in Play mode. Baselines live in `ImageBaselines/<graphics API>/` next to `Assets`. Reports live in `Library/InfinityImageRegression/`. |

Create menus are GameObject > Light > Infinity, GameObject > Rendering > Infinity, GameObject > 3D Object > Infinity > MeshEntity, Assets > Create > Rendering > Infinity, and Assets > Create > Shader Graph > Infinity.

Custom terrain sectors are rebuilt from Terrain Component > Rebuild Sector From Unity Terrain. The action supports Undo and multi-object editing. Saving a scene does not rebuild sectors. Disabling the component releases runtime storage and keeps the authored sectors.

The command-line adapter under `Editor/Cli` is compiled only with `INFINITY_UNITY_CLI`. It has no menu. Commands do not save assets.

## Inspectors

| Object | Sections |
|---|---|
| Camera | Projection, Rendering, Environment (Volume Mask, Volume Trigger, Super Resolution), Frame Settings, Output |
| Light | General, Shape, Emission, Baking, Cookie & IES, Shadows (caster layers, filter, update mode, Request Shadow Map Rendering, contact shadows), Volumetric, Layers & Distance. Additional Properties reveals the less common fields. Scene handles edit range, angles and the source shape |
| Mesh | State (Mobility), Mesh, Lighting, Rendering |
| Decal | General, Projection |
| Volume Profile | The Core RP component list |
| Pipeline asset | Rendering, Lighting & Shadows (including shadow-filter quality), Post-processing & Output, Frame Settings (Cameras and Reflection Probe Captures), Default Materials. Runtime shaders are named in a help box and resolve from Global Settings |
| Atmospherical Profile | Planet, Scatter, Quality. Reset to Earth restores the packaged physical range |
| Diffusion Profile | Scatter, plus the index of this profile on the pipeline asset |
| InfinityLit | Surface Options, Surface Inputs, Subsurface when the subsurface control is enabled or the foldout was previously open, Baked Emission, Advanced / Render State |
| Shader Graph Lit | Surface Options, Subsurface, Advanced / Render State |
| Terrain | General, and Rebuild Sector From Unity Terrain |
| Additional reflection-probe data | Influence, and Sphere Radius when the influence is a sphere |

Camera Rendering explains that MSAA, the built-in HDR toggle and command-buffer injection are not Infinity features. Custom Frame Settings must be enabled before the per-feature overrides appear.

## Render Graph Viewer

Window > Analysis > Infinity Render Graph Viewer shows the last compiled graph for each camera while the window is open. The window title is Render Graph Viewer. The toolbar selects a graph snapshot, then Pause, Culled, Imported, Unused, a name filter, Copy and Clear.

The summary line shows the frame, pass count, culled count, async count, segment count, resource count and peak alias memory in kibibytes.

Columns are passes in record order. A header is blue on the graphics queue, orange on async compute, and gray when the pass is culled. The tooltip names the pass, its type, queue, cull state, submission segment, and native-pass group and subpass when the pass was merged. Rows are resources. An imported resource has a dot. A cell is green for a read, red for a write, and yellow for both. A blue band is the pooled lifetime from the allocating pass to the releasing pass.

Selecting a pass shows type, queue, segment, native pass, cull reason or the reason culling is disabled, plus waits, signals, reads, writes, allocations and releases. Selecting a resource shows kind, imported or pooled, size, physical slot, first and last use, allocating pass, releasing pass, writers and readers.
