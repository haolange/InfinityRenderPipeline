using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.Reflection;
using InfinityTech.Rendering.Decal;
using InfinityTech.Rendering.AnalyticShadow;
using InfinityTech.Rendering.Temporal;

namespace InfinityTech.Rendering.Pipeline
{
    // This frame's decisions for one camera, made before culling. Read-only once PrepareCamera returns.
    internal readonly struct CameraSetup
    {
        internal readonly Camera camera;
        // Cross-frame owner of this camera's history, caches and transaction state.
        internal readonly CameraFrameState state;
        internal readonly CameraRenderPolicy policy;
        internal readonly ResolvedFrameSettings frameSettings;
        internal readonly CameraDimensionDescriptor dimensions;
        internal readonly EHistoryReset historyReset;
        internal readonly TemporalFrameRequirements temporalRequirements;
        internal readonly bool postProcessingEnabled, sceneLightingEnabled;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal readonly EDebugView debugView;
#endif
        internal readonly OutputTransformDecision outputDecision;
        internal readonly VolumeSnapshot volumes;
        internal readonly AtmosphereParameter atmosphere;
        internal readonly FeatureSupportContext featureSupport;

        internal bool resetsHistory => historyReset != EHistoryReset.None;
        internal bool temporalActive => temporalRequirements.isActive;

        internal CameraSetup(Camera camera, CameraFrameState state, CameraRenderPolicy policy, in ResolvedFrameSettings frameSettings,
            in CameraDimensionDescriptor dimensions, EHistoryReset historyReset, bool postProcessingEnabled,
            bool sceneLightingEnabled,
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            EDebugView debugView,
#endif
            in OutputTransformDecision outputDecision, in VolumeSnapshot volumes, in AtmosphereParameter atmosphere,
            in FeatureSupportContext featureSupport, in TemporalFrameRequirements temporalRequirements = default)
        {
            this.camera = camera;
            this.state = state;
            this.policy = policy;
            this.frameSettings = frameSettings;
            this.dimensions = dimensions;
            this.historyReset = historyReset;
            this.postProcessingEnabled = postProcessingEnabled;
            this.sceneLightingEnabled = sceneLightingEnabled;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            this.debugView = debugView;
#endif
            this.outputDecision = outputDecision;
            this.volumes = volumes;
            this.atmosphere = atmosphere;
            this.featureSupport = featureSupport;
            this.temporalRequirements = temporalRequirements.displaySize.x > 0
                ? temporalRequirements
                : TemporalFrameRequirements.Disabled(dimensions.displaySize);
        }
    }

    // What culling produced for one camera: Unity's snapshot and the scene world's view layers built from it. Read-only
    // once CullCamera returns.
    internal readonly struct SceneCullingResults
    {
        internal readonly CullingResults cullingResults;
        // The camera's mesh culling context: the main view of every camera-facing draw list.
        internal readonly MeshCullingContext mainCulling;
        // The light module's view layer, built for this camera: records, bins, shadow allocation, atlas entries.
        internal readonly LightContext lights;
        // The reflection module's view layer, built for this camera: priority-ordered probe records and 3D cluster index.
        internal readonly ReflectionContext reflections;
        // The decal module's view layer: raster decal counts and clustered decal records.
        internal readonly DecalContext decals;
        // The analytic shadow module's view layer: dual spatial cluster indices for indirect & direct visibility.
        internal readonly AnalyticShadowContext analyticShadows;
        internal readonly MeshWorld meshes;
        // Published decal count of this frame's scene transaction.
        internal readonly int decalCount;

        internal SceneCullingResults(in CullingResults cullingResults, in MeshCullingContext mainCulling, LightContext lights,
            ReflectionContext reflections, DecalContext decals, AnalyticShadowContext analyticShadows, MeshWorld meshes, int decalCount)
        {
            this.cullingResults = cullingResults;
            this.mainCulling = mainCulling;
            this.lights = lights;
            this.reflections = reflections;
            this.decals = decals;
            this.analyticShadows = analyticShadows;
            this.meshes = meshes;
            this.decalCount = decalCount;
        }
    }

    // Everything a pass reads while recording one camera: the three clocks flattened. CameraSetup (this camera's
    // decisions), SceneCullingResults (what culling produced) and FrameContext (this Render call's engine facts) are
    // immutable, so the copies cannot diverge. Passes take it by `in` and never store it; the graph is executed and
    // cleared before the next camera, so nothing recorded outlives it.
    internal readonly struct ViewContext
    {
        internal readonly Camera camera;
        internal readonly CameraFrameState state;
        internal readonly CameraRenderPolicy policy;
        internal readonly ResolvedFrameSettings frameSettings;
        internal readonly CameraDimensionDescriptor dimensions;
        internal readonly EHistoryReset historyReset;
        internal readonly TemporalFrameRequirements temporalRequirements;
        internal readonly bool postProcessingEnabled, sceneLightingEnabled;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal readonly EDebugView debugView;
#endif
        internal readonly OutputTransformDecision outputDecision;
        internal readonly VolumeSnapshot volumes;
        internal readonly AtmosphereParameter atmosphere;
        internal readonly FrameFeatureSet features;
        internal readonly SceneColorKeys sceneColor;
        // The geometry draw allocates the sideband because one of its readers records this camera.
        internal readonly bool sideband;
        internal readonly HistoryCache historyCache;
        internal readonly CameraUniform cameraUniform;
        internal readonly CullingResults cullingResults;
        internal readonly MeshCullingContext mainCulling;
        internal readonly LightContext lights;
        internal readonly ReflectionContext reflections;
        internal readonly DecalContext decals;
        internal readonly AnalyticShadowContext analyticShadows;
        internal readonly MeshWorld meshes;
        internal readonly int decalCount;
        internal readonly ScriptableRenderContext scriptableRenderContext;
        internal readonly int frame;
        internal readonly ShadowmaskMode shadowmaskMode;

        internal bool resetsHistory => historyReset != EHistoryReset.None;
        internal bool temporalActive => temporalRequirements.isActive;

        internal ViewContext(in FrameContext frameContext, in CameraSetup setup, in SceneCullingResults scene)
        {
            camera = setup.camera;
            state = setup.state;
            policy = setup.policy;
            frameSettings = setup.frameSettings;
            dimensions = setup.dimensions;
            historyReset = setup.historyReset;
            temporalRequirements = setup.temporalRequirements;
            postProcessingEnabled = setup.postProcessingEnabled;
            sceneLightingEnabled = setup.sceneLightingEnabled;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugView = setup.debugView;
#endif
            outputDecision = setup.outputDecision;
            volumes = setup.volumes;
            atmosphere = setup.atmosphere;
            features = setup.state.features;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            sceneColor = new SceneColorKeys(setup.state.features, setup.temporalActive, setup.debugView != EDebugView.None);
#else
            sceneColor = new SceneColorKeys(setup.state.features, setup.temporalActive);
#endif
            sideband = ResolveSideband(setup.state.features, setup.featureSupport.renderingLayers, scene.lights);
            historyCache = setup.state.historyCache;
            cameraUniform = setup.state.cameraUniform;
            cullingResults = scene.cullingResults;
            mainCulling = scene.mainCulling;
            lights = scene.lights;
            reflections = scene.reflections;
            decals = scene.decals;
            analyticShadows = scene.analyticShadows;
            meshes = scene.meshes;
            decalCount = scene.decalCount;
            scriptableRenderContext = frameContext.scriptableRenderContext;
            frame = frameContext.frame;
            shadowmaskMode = frameContext.shadowmaskMode;
        }

        // Readers of the sideband: rendering-layer channels, mixed-light baked visibility and subsurface scattering.
        // Deferred shading reads G, B and A when the attachment exists and does not write it. Screen-space occlusion,
        // SSGI and analytic indirect occlusion do not allocate it. Without a reader it is not allocated.
        static bool ResolveSideband(FrameFeatureSet features, bool renderingLayers, LightContext lights)
        {
            return renderingLayers
                || lights.HasMixedLights
                || features.ShouldRecord(EFrameFeature.SubsurfaceScattering);
        }
    }
}
