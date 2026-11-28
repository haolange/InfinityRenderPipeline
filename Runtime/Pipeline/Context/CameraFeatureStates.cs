using System;
using InfinityTech.Rendering.AnalyticShadow;
using InfinityTech.Rendering.Decal;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.Reflection;
using InfinityTech.Rendering.Temporal;
using Unity.Mathematics;

namespace InfinityTech.Rendering.Pipeline
{
    /// <summary>
    /// Camera-lifetime screen-space effect acceptance state (Hi-Z, SSR, SSGI, GTAO) owned via typed extension slot.
    /// </summary>
    internal sealed class ScreenSpaceCameraState : ICameraStateExtension
    {
        internal static readonly CameraExtensionKey<ScreenSpaceCameraState> Key = CameraExtensionKey<ScreenSpaceCameraState>.Default;
        internal static readonly Func<ResourcePool, ScreenSpaceCameraState> Factory = _ => new ScreenSpaceCameraState();

        internal bool acceptedHiZ;
        internal bool acceptedSSR;
        internal bool acceptedSSGI;
        internal bool acceptedGTAO;
        internal int acceptedSSGIFrameIndex;

        public void BeginFrame() { }

        public void CommitFrame(FrameFeatureSet features, int frameCount)
        {
            if (features != null && features.IsProduced(EFrameFeature.SSGI))
            {
                acceptedSSGIFrameIndex = frameCount;
            }
            acceptedHiZ = features != null && features.IsProduced(EFrameFeature.HiZ);
            acceptedSSR = features != null && features.IsProduced(EFrameFeature.SSR);
            acceptedSSGI = features != null && features.IsProduced(EFrameFeature.SSGI);
            acceptedGTAO = features != null && features.IsProduced(EFrameFeature.GTAO);
        }

        public void RollbackFrame() { }
        public void CameraFailed(bool queueUncertain) { }
        public void FlushRetired(ResourceReuseFence fence) { }
        public void Dispose(ResourceReuseFence fence) { }
        public void Dispose() { }
    }

    /// <summary>
    /// Camera-lifetime constant buffers for shadows, local lights, reflections, decals and analytic occluders.
    /// </summary>
    internal sealed class LightingCameraState : ICameraStateExtension
    {
        internal static readonly CameraExtensionKey<LightingCameraState> Key = CameraExtensionKey<LightingCameraState>.Default;
        internal static readonly Func<ResourcePool, LightingCameraState> Factory = _ => new LightingCameraState();

        internal readonly UniformBuffer<ShadowUniform> shadowUniform = new UniformBuffer<ShadowUniform>();
        internal readonly UniformBuffer<LightLoopUniform> lightLoopUniform = new UniformBuffer<LightLoopUniform>();
        internal readonly UniformBuffer<ReflectionUniform> reflectionUniform = new UniformBuffer<ReflectionUniform>();
        internal readonly UniformBuffer<DecalUniform> decalUniform = new UniformBuffer<DecalUniform>();
        internal readonly UniformBuffer<AnalyticShadowUniform> analyticShadowUniform = new UniformBuffer<AnalyticShadowUniform>();

        public void BeginFrame() { }
        public void CommitFrame(FrameFeatureSet features, int frameCount) { }
        public void RollbackFrame() { }
        public void CameraFailed(bool queueUncertain) { }
        public void FlushRetired(ResourceReuseFence fence) { }
        public void Dispose(ResourceReuseFence fence) => Dispose();

        public void Dispose()
        {
            shadowUniform.Dispose();
            lightLoopUniform.Dispose();
            reflectionUniform.Dispose();
            decalUniform.Dispose();
            analyticShadowUniform.Dispose();
        }
    }

    /// <summary>
    /// Camera-lifetime atmosphere and post LUT caches participating in graph frame transactions.
    /// </summary>
    internal sealed class AtmosphereCameraState : ICameraStateExtension
    {
        internal static readonly CameraExtensionKey<AtmosphereCameraState> Key = CameraExtensionKey<AtmosphereCameraState>.Default;
        internal static readonly Func<ResourcePool, AtmosphereCameraState> Factory = pool => new AtmosphereCameraState(pool);

        internal AtmosphereViewCache atmosphereViewCache;
        internal CombineLutCache combineLutCache;

        AtmosphereCameraState(ResourcePool pool)
        {
            atmosphereViewCache = new AtmosphereViewCache(pool);
            combineLutCache = new CombineLutCache(pool);
        }

        public void BeginFrame()
        {
            atmosphereViewCache?.BeginFrame();
            combineLutCache?.BeginFrame();
        }

        public void CommitFrame(FrameFeatureSet features, int frameCount)
        {
            atmosphereViewCache?.CommitFrame();
            combineLutCache?.CommitFrame();
        }

        public void RollbackFrame()
        {
            atmosphereViewCache?.RollbackPending();
            combineLutCache?.RollbackPending();
        }

        public void CameraFailed(bool queueUncertain)
        {
            atmosphereViewCache?.CameraFailed(queueUncertain);
            combineLutCache?.CameraFailed(queueUncertain);
        }

        public void FlushRetired(ResourceReuseFence fence)
        {
            atmosphereViewCache?.FlushRetired(fence);
            combineLutCache?.FlushRetired(fence);
        }

        public void Dispose(ResourceReuseFence fence)
        {
            if (atmosphereViewCache != null)
            {
                atmosphereViewCache.Dispose(fence);
                atmosphereViewCache = null;
            }
            if (combineLutCache != null)
            {
                combineLutCache.Dispose(fence);
                combineLutCache = null;
            }
        }

        public void Dispose() => Dispose(default);
    }

    /// <summary>
    /// Camera-lifetime mesh transform version history for object motion vectors.
    /// </summary>
    internal sealed class MeshMotionCameraState : ICameraStateExtension
    {
        internal static readonly CameraExtensionKey<MeshMotionCameraState> Key = CameraExtensionKey<MeshMotionCameraState>.Default;
        internal static readonly Func<ResourcePool, MeshMotionCameraState> Factory = _ => new MeshMotionCameraState();

        internal readonly MeshMotionHistory history = new MeshMotionHistory();

        public void BeginFrame() { }
        public void CommitFrame(FrameFeatureSet features, int frameCount) => history.Commit();
        public void RollbackFrame() => history.Rollback();
        public void CameraFailed(bool queueUncertain) => history.Rollback();
        public void FlushRetired(ResourceReuseFence fence) { }
        public void Dispose(ResourceReuseFence fence) => history.Clear();
        public void Dispose() => history.Clear();
    }

    /// <summary>
    /// Camera-lifetime state for unified Temporal Reconstruction (PRD §9.4 & §10.1): tracks active backend,
    /// reconstruction mode, accepted history color view, and backend-switch resets across frames.
    /// </summary>
    internal sealed class TemporalCameraState : ICameraStateExtension
    {
        internal static readonly CameraExtensionKey<TemporalCameraState> Key = CameraExtensionKey<TemporalCameraState>.Default;
        internal static readonly Func<ResourcePool, TemporalCameraState> Factory = _ => new TemporalCameraState();

        internal ETemporalBackend committedBackend;
        internal ETemporalReconstructionMode committedMode;
        internal int2 committedDisplaySize;
        internal TemporalFrameRequirements preparedRequirements;
        internal bool hasPreparedBackend;
        internal bool acceptedTemporalColor;
        internal TemporalColorView temporalColorView;

        internal bool RequiresBackendSwitchReset(in TemporalFrameRequirements current)
        {
            if (committedBackend == ETemporalBackend.None || !current.isActive)
            {
                return current.isActive != (committedBackend != ETemporalBackend.None);
            }
            return committedBackend != current.activeBackend || committedMode != current.mode;
        }

        internal void PrepareFrame(in TemporalFrameRequirements requirements)
        {
            preparedRequirements = requirements;
            hasPreparedBackend = true;
            temporalColorView = default;
        }

        public void BeginFrame()
        {
            temporalColorView = default;
        }

        public void CommitFrame(FrameFeatureSet features, int frameCount)
        {
            bool producedTemporal = features != null && features.IsProduced(EFrameFeature.TemporalReconstruction);
            acceptedTemporalColor = producedTemporal;
            if (hasPreparedBackend)
            {
                committedBackend = preparedRequirements.activeBackend;
                committedMode = preparedRequirements.mode;
                committedDisplaySize = preparedRequirements.displaySize;
                hasPreparedBackend = false;
            }
        }

        public void RollbackFrame() => hasPreparedBackend = false;
        public void CameraFailed(bool queueUncertain) => hasPreparedBackend = false;
        public void FlushRetired(ResourceReuseFence fence) { }
        public void Dispose(ResourceReuseFence fence) => Dispose();

        public void Dispose()
        {
            committedBackend = ETemporalBackend.None;
            committedDisplaySize = int2.zero;
            acceptedTemporalColor = false;
            temporalColorView = default;
        }
    }
}
