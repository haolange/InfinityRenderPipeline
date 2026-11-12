using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

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
        internal readonly ETemporalResolve temporalResolve;
        internal readonly bool postProcessingEnabled, sceneLightingEnabled;
        internal readonly EDebugView debugView;
        internal readonly OutputTransformDecision outputDecision;
        internal readonly VolumeSnapshot volumes;
        internal readonly AtmosphereParameter atmosphere;
        internal readonly FeatureSupportContext featureSupport;

        internal bool resetsHistory => historyReset != EHistoryReset.None;

        internal CameraSetup(Camera camera, CameraFrameState state, CameraRenderPolicy policy, in ResolvedFrameSettings frameSettings,
            in CameraDimensionDescriptor dimensions, EHistoryReset historyReset, ETemporalResolve temporalResolve, bool postProcessingEnabled,
            bool sceneLightingEnabled, EDebugView debugView, in OutputTransformDecision outputDecision, in VolumeSnapshot volumes,
            in AtmosphereParameter atmosphere, in FeatureSupportContext featureSupport)
        {
            this.camera = camera;
            this.state = state;
            this.policy = policy;
            this.frameSettings = frameSettings;
            this.dimensions = dimensions;
            this.historyReset = historyReset;
            this.temporalResolve = temporalResolve;
            this.postProcessingEnabled = postProcessingEnabled;
            this.sceneLightingEnabled = sceneLightingEnabled;
            this.debugView = debugView;
            this.outputDecision = outputDecision;
            this.volumes = volumes;
            this.atmosphere = atmosphere;
            this.featureSupport = featureSupport;
        }
    }

    // What culling produced for one camera. Read-only once CullCamera returns.
    internal readonly struct CameraScene
    {
        internal readonly CullingResults cullingResults;
        internal readonly MeshView mainView;

        internal CameraScene(in CullingResults cullingResults, in MeshView mainView)
        {
            this.cullingResults = cullingResults;
            this.mainView = mainView;
        }
    }

    // Everything a pass reads while recording one camera. Passes take it by `in` and never store it; the graph
    // is executed and cleared before the next camera, so nothing recorded outlives it. It flattens CameraSetup
    // and CameraScene, both immutable, so the copies cannot diverge. The mesh view is not here: occlusion
    // preparation replaces it during recording, so RecordCamera carries it explicitly.
    internal readonly struct CameraRecordContext
    {
        internal readonly Camera camera;
        internal readonly CameraFrameState state;
        internal readonly CameraRenderPolicy policy;
        internal readonly ResolvedFrameSettings frameSettings;
        internal readonly CameraDimensionDescriptor dimensions;
        internal readonly EHistoryReset historyReset;
        internal readonly ETemporalResolve temporalResolve;
        internal readonly bool postProcessingEnabled, sceneLightingEnabled;
        internal readonly EDebugView debugView;
        internal readonly OutputTransformDecision outputDecision;
        internal readonly VolumeSnapshot volumes;
        internal readonly AtmosphereParameter atmosphere;
        internal readonly CullingResults cullingResults;
        internal readonly FrameFeatureSet features;
        internal readonly HistoryCache historyCache;
        internal readonly CameraUniform cameraUniform;

        internal bool resetsHistory => historyReset != EHistoryReset.None;

        internal CameraRecordContext(in CameraSetup setup, in CameraScene scene)
        {
            camera = setup.camera;
            state = setup.state;
            policy = setup.policy;
            frameSettings = setup.frameSettings;
            dimensions = setup.dimensions;
            historyReset = setup.historyReset;
            temporalResolve = setup.temporalResolve;
            postProcessingEnabled = setup.postProcessingEnabled;
            sceneLightingEnabled = setup.sceneLightingEnabled;
            debugView = setup.debugView;
            outputDecision = setup.outputDecision;
            volumes = setup.volumes;
            atmosphere = setup.atmosphere;
            cullingResults = scene.cullingResults;
            features = setup.state.features;
            historyCache = setup.state.historyCache;
            cameraUniform = setup.state.cameraUniform;
        }
    }
}
