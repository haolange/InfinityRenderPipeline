# Passes and pipelines

This page is for code that records into the pipeline: a new pass, or a pipeline derived from `InfinityRenderPipelineBase`. It says where each piece lives and which call to make. The contracts each piece keeps are in [ARCHITECTURE.md](../../ARCHITECTURE.md), linked by section below. The rules for writing the code are in [AGENTS.md](../../AGENTS.md).

The base constructor is `private protected` and passes are `internal`, so a derived pipeline and its passes are written in the runtime assembly, or in an assembly that sees its internals, as `TestRenderPipeline` in `Tests/Editor/` does.

## Three contexts

Each context belongs to one clock. Read a fact from the context of its clock and do not keep it longer.

| Context | Lives for | Holds | Read by |
|---|---|---|---|
| `RenderContext` | The pipeline instance | Resource pool, scene world, render graph, shared atmosphere cache, Adaptive Probe Volumes, camera states, pipeline-wide frame transactions | The base, and the derived pipeline's constructor, which hands its services to the passes |
| `FrameContext` | One `Render` call | Scriptable render context, frame number, shadowmask mode | The base. Passes get these facts through `ViewContext` |
| `ViewContext` | One camera's recording | Camera, dimensions, temporal resolve and history reset, volumes, atmosphere, resolved features, scene-color keys, history cache, camera uniforms, Unity culling results, the main mesh culling context, the light context, the mesh world and the frame facts | Every pass, by `in` |

A pass never names `RenderContext` or the pipeline, and never stores a `ViewContext`. See [§1](../../ARCHITECTURE.md#1-system) and [§4](../../ARCHITECTURE.md#4-camera-frame).

## Scene world

`renderContext.sceneWorld` is the pipeline's derived copy of the scene, one module per scene type:

| Module | Authoring component | What a pass reads |
|---|---|---|
| `meshes` | `MeshComponent` | Mesh views and draw lists, below |
| `lights` | `InfinityAdditionalLightData` | `context.lights`, the camera's `LightContext` |
| `probes` | `InfinityAdditionalReflectionProbeData` | The probe entries of `context.lights` |
| `decals` | `DecalComponent` | `context.decalCount`, through the DBuffer feature |
| `terrains` | `TerrainComponent` | Nothing yet; terrain is in development |

A component registers in its type's `ComponentRegistry` while it is enabled. Each frame `SceneWorld.Update` rebuilds the tables whose registry changed and publishes them together, so every camera of a frame sees one scene, and a replaced pipeline rebuilds every table from the registries. A new scene type is a registry on its component, a module with a `SceneTable`, and a place in that one transaction. See [Scene world](../../ARCHITECTURE.md#scene-world).

## Writing a pass

A pass is an `internal sealed class XxxPass` in `Runtime/Pipeline/Pass/<domain>/`, with its `XxxPassData` struct and, when it binds shader properties, an `XxxPassUtilityData` class of property IDs.

- The constructor takes what lives as long as the pipeline: the `RGBuilder`, `InfinityRenderPipelineResources` (kernels, shaders and blit passes, read at record time), the settings snapshot, the caches, atlases and observers it uses, the passes it calls, and the `MeshPassId` of each mesh pass it draws.
- The record method takes `in ViewContext` and the handles it consumes, such as the mesh view it draws, and returns the handles it produces. It decides from `context.features` whether to record, reads optional inputs with `QueryTexture`, and registers only its own output key.
- A pass that produces a required feature calls `context.features.ThrowIfCannotProduce` before it records and `MarkProduced` after.
- The pipeline builds one instance in its constructor and calls it from `RecordCamera`.

```csharp
internal sealed class DepthPass
{
    readonly RGBuilder m_RGBuilder;
    readonly MeshPassId m_DepthMeshPass;

    internal DepthPass(RGBuilder graph, in InfinityRenderPipelineAsset.Settings settings, MeshPassId depthMeshPass) { /* store */ }

    internal void RasterDepth(in ViewContext context, in RGMeshViewHandle viewHandle)
    {
        context.features.ThrowIfCannotProduce(EFrameFeature.Depth);
        RGMeshDrawListHandle drawsHandle = m_RGBuilder.CreateMeshDrawList(viewHandle, m_DepthMeshPass);
        // Create and register DepthBuffer, add the raster pass, UseMeshDrawList; execute calls SubmitMeshDrawList.
        context.features.MarkProduced(EFrameFeature.Depth);
    }
}
```

`static_check passroot` rejects a pass that names the pipeline or the render context. Pass types, attachments, per-pass constants and profiling names are in [§5](../../ARCHITECTURE.md#5-render-graph).

## Writing a pipeline

A pipeline derives `InfinityRenderPipelineBase`, gives the base constructor its `PipelineCapabilities`, builds its passes and overrides `RecordCamera`. The base owns everything else in a frame: camera preparation, culling, the light context, volumes, history, graph execution and submission.

```csharp
internal sealed class DepthOnlyPipeline : InfinityRenderPipelineBase
{
    static readonly MeshPassDesc[] s_MeshPasses =
    {
        new MeshPassDesc("RasterDepthPass", "Depth", 0, 2999),
        new MeshPassDesc("RasterShadowPass", "Shadow", 0, 2999, EMeshEntityFlags.CastShadow)
    };
    static readonly PipelineCapabilities s_Capabilities = new PipelineCapabilities(
        new[] { EFrameFeature.Depth, EFrameFeature.Display }, new[] { EFrameFeature.Depth, EFrameFeature.Display },
        asyncCompute: false, meshPasses: s_MeshPasses, shadowCasterPass: new MeshPassId(1));

    readonly DepthPass m_DepthPass;

    internal DepthOnlyPipeline(InfinityRenderPipelineAsset asset) : base(asset, s_Capabilities)
    {
        m_DepthPass = new DepthPass(renderContext.graph, settings, new MeshPassId(0));
    }

    private protected override void RecordCamera(in ViewContext context, CommandBuffer cmdBuffer)
    {
        RGMeshViewHandle viewHandle = renderContext.graph.CullMeshView(new MeshViewDesc(context.mainCulling, default, MeshOccluder.None));
        m_DepthPass.RasterDepth(context, viewHandle);
        // Present: FinalOutputPass.ImportCameraOutput, then RasterFinalOutput, then MarkProduced(EFrameFeature.Display).
    }
}
```

| Capability | Effect |
|---|---|
| `features` | The features the pipeline records. Any other feature does not record and reports "This pipeline does not record it." as its reason; TAA and super resolution resolve only when recorded |
| `requiredProducers` | Every camera must produce these. Resources that cannot support one fail when they resolve; the base throws after `RecordCamera` when a camera did not produce one |
| `asyncCompute` | Whether the graph may use the async compute queue on a device that has one |
| `meshPasses` | The mesh pass table. The mesh world builds one pass cache per entry; passes name entries by `MeshPassId` |
| `shadowCasterPass` | The entry whose members cast shadows. Shadow allocation and caster bounds collect from it |

`RecordCamera` records the pass list in submission order. `cmdBuffer` is the camera command buffer for constants uploaded ahead of the graph, as the desktop pipeline uploads its shadow and light-loop constants. A constructor that fails after the base constructor calls `ReleaseOwnedResources` and rethrows; a pipeline whose passes own resources disposes them before `base.Dispose`. `TestRenderPipeline` is the smallest complete example. Current limits for a pipeline other than the desktop one are in [§16](../../ARCHITECTURE.md#16-limits-present-in-the-source).

## Scene color

`context.sceneColor` names the graph key of each scene-color stage, derived from the camera's features before recording:

| Stage | Written by |
|---|---|
| `opaque` | Deferred and forward lighting (`LightingBuffer`), or the screen-space composite when SSR or SSGI records |
| `fogged` | The fog composite when volumetric fog or clouds record; otherwise the opaque stage |
| `resolved` | Temporal anti-aliasing or super resolution; otherwise the fogged stage |
| `display` | Post processing, or the debug view when one is active |

A reader asks for a stage, for example `m_RGBuilder.scoper.QueryTexture(context.sceneColor.fogged)`. A writer registers its own key and never moves or republishes another. A new stage producer adds its condition to `SceneColorKeys`, the same condition it records under. See [§5](../../ARCHITECTURE.md#5-render-graph) and [§13](../../ARCHITECTURE.md#13-temporal-resolve-and-output).

## Mesh views and draw lists

1. The pipeline culls views with `CullMeshView(new MeshViewDesc(cullingContext, motion, occluder))` and receives an `RGMeshViewHandle`. The desktop pipeline's `MeshOcclusionPass.CullMainViews` returns the camera's depth, recovery and main views; without two-stage occlusion they are one frustum view. Shadow passes cull one light view per cascade or local face with `MeshOccluder.None`.
2. A pass receives the view it draws as a parameter and creates its list with `CreateMeshDrawList(viewHandle, meshPass)`. A view holds one list per pass, and a pass that needs previous transforms needs a view with mesh motion.
3. The raster pass consumes the list with `UseMeshDrawList` and draws it in execute with `SubmitMeshDrawList`.

Production is scheduled by the graph: on the GPU one visibility producer per view with a nonempty list and one producer per nonempty list, on the CPU one visibility job per view with a live list and one gather per list, which filters that job's result; translucent color passes always draw on the CPU. Two views with equal descriptors are two views, so pass handles, not descriptors. The full mechanism is [§7](../../ARCHITECTURE.md#7-mesh-drawing-pipeline); authoring of mesh content is [Mesh content](mesh.md).
