# Mesh content

`MeshComponent` objects are drawn by the pipeline's own mesh path instead of Unity renderer lists. Native `MeshRenderer`, `SkinnedMeshRenderer`, terrain and particles keep Unity culling and renderer lists, including lightmaps and light probes. The implementation is [ARCHITECTURE.md §7](../ARCHITECTURE.md#7-mesh-drawing-pipeline).

A pass records `new MeshDrawListDesc(view, InfinityPassIDs.RasterGBufferPass)` with `CreateMeshDrawList`, consumes it with `UseMeshDrawList`, and submits it with `SubmitMeshDrawList`. The list is `MeshDrawCall`s merged from `MeshDrawInstance`s of cached `MeshDrawCommand`s. The world creates the nine pass caches with itself.

## Authoring

1. Create a `MeshAsset` (Assets > Create > Rendering > Infinity > Static Mesh Asset), or build one from a prefab with Assets > AssetActions > Primitive > BuildMeshAssetFromPrefab. The prefab needs a `LODGroup` whose levels each hold one renderer at the prefab root's transform and no renderer outside the levels, or a `MeshFilter` with a `MeshRenderer` on the root and no other renderer. Other prefabs are rejected with the reason, because a level is one mesh drawn in the component's space.
2. Add a Mesh Component (Add Component > InfinityRenderer > Mesh Component, or GameObject > 3D Object > Infinity > MeshEntity) and assign the asset. Assigning an asset copies its materials into the component once. With no asset, the inspector shows a **Create MeshAsset from Mesh** field: pick a saved mesh and press **Create MeshAsset**. It creates (or reuses) a one-level asset under `Assets/InfinityMeshAssets/` and assigns it without changing the component's materials.
3. Use materials whose shader declares the SubShader tag `InfinityMeshInstancing = True`, such as `InfinityPipeline/InfinityLit-Instanced`. The material's surface route and translucent stage decide which passes draw it.

A component with no asset creates no entity and draws nothing. A component whose asset is invalid, or whose material lacks the tag or a pass its route needs, is reported in the Console with the object and is not drawn. Other components still draw.

## MeshAsset

| Field | Meaning |
|---|---|
| `meshes` | One mesh per LOD level. `meshes[i]` is LOD *i*. The same mesh may fill several levels. |
| `lODInfos[i].screenSize` | Lower screen-size bound of LOD *i*, as `LODGroup` transition heights. Bounds strictly decrease within [0, 1]. |
| `lODInfos[i].materialSlot[s]` | Material slot of submesh *s* of LOD *i*. A submesh without an entry draws nothing. |
| `materials` | Default materials, copied into a component when the asset is assigned in the inspector (the `MeshAsset` property does not copy them). |
| `fadeWidth` | Cross-fade band, from 0.01 to 0.99 of the gap to the previous bound. Default 0.2. |

An asset has 1 to 8 levels and one mesh per level. The component's own material array, indexed by slot, is what draws. After a script edits an asset, call `MeshComponent.NotifyGeometryChanged(asset)`. Inspector and Undo edits, and destroying the asset, notify on their own.

## LOD

LOD *i* draws while the object's screen size is at least its bound. Below the last bound the object is not drawn, unless that bound is 0. Screen size is the largest half-extent of the object's world bounds divided by the distance and by tan(fov / 2), scaled by **Quality Settings > LOD Bias**. That is the measure `LODGroup` transition heights use for an unrotated object; rotation can grow the world bounds by up to √3. Orthographic cameras divide by the orthographic size instead.

Inside the band above a bound, opaque passes (depth, GBuffer, forward, motion and shadow) dither between the outgoing and the incoming level, so every pixel shows exactly one of them. Above a positive last bound there is no incoming level, so the object dithers out to nothing. Translucent passes draw the selected level only and switch without a fade. Shadow views select the level of the camera that renders them. Ray tracing uses LOD 0. GPU frustum culling of one camera is one dispatch; a view that already has an occlusion result reads that result.

## Component settings

| Setting | Effect |
|---|---|
| Mobility | Dynamic poses are read every frame. Static poses are read when the entity is created and after `NotifyEntityChanged()`. In the Editor they also refresh after an undoable edit of the object or a parent (Scene view handles, the Transform inspector, layer changes), and, in Edit mode, after undo, redo and hierarchy changes such as reparenting or moving between scenes. **Refresh Renderer Proxy** on the pipeline asset, and the first frame of a Player, refresh every static pose. |
| Cast Shadow | Off by default. Static and Dynamic both join the shadow pass. |
| Motion Vector | Object motion joins the motion pass, which reads the previous transform. |
| Rendering Layer | The shared 8-bit mesh and light mask. Lighting reads it from the GBuffer and shadow views cull by it. |
| Render Priority | Orders translucent content before distance. |
| Visible | Visibility in every view. |

Mesh components store no lightmap and no per-instance probe data. A material may sample Adaptive Probe Volumes in world space.

## Backends

`meshBackendPolicy` on the [pipeline asset](pipeline-asset.md) selects Auto, CPU direct or GPU indirect. A camera or test override wins, then the Rendering Debugger override, then the asset. Auto and GPU indirect use the GPU when the device supports compute and instancing.

- CPU direct culls and sorts in Burst jobs and issues one instanced draw per run of instances that share a command.
- GPU indirect culls and compacts in compute and issues indirect draws in state order. It has no per-instance distance order and supports two-phase Hi-Z occlusion.
- Translucent color passes (T0/T1/T2) are sorted back to front and always use the CPU backend. The translucent depth pass sorts front to back on the selected backend.

The Rendering Debugger's Infinity Mesh panel shows entity, element, pass-cache and command counts, the requested and actual backend, and the reason.

## Scripting

- `MeshAsset`, `Mobility`, `SetMaterials` and the instance properties mark the component for the next frame's update. `Materials` is read-only.
- `NotifyGeometryChanged(Mesh)` after editing a mesh in place. `NotifyGeometryChanged(MeshAsset)` after editing an asset from script.
- `MaterialRouteUtility.ApplyPassState(material)` after changing `_SurfaceRoute` or `_TranslucentStage`. `MeshComponent.NotifyMaterialRoutingChanged(material)` after changing a render queue directly.
- `NotifyEntityChanged()` after moving a static object, or changing its GameObject layer or scene, from script.

Ordinary material properties need no notification.

## Limits

- MeshComponent translucency and native translucent renderers are sorted in two lists. Each list is back to front, but there is no distance order between them.
- Shader Graph materials have no mesh-instancing path and are not drawn by `MeshComponent`.
- LOD selection works per object. There is no per-submesh or per-cluster LOD.
- Receive Shadow and Affect Indirect Lighting are stored on the entity, but no pass reads them yet.

Projects from 0.4 migrate with the steps in [Upgrade 0.4 to 0.5](upgrade-0.4-to-0.5.md).
