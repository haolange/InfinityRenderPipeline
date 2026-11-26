using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Temporal;

namespace InfinityTech.Rendering.Pipeline
{
    // Why a camera discards its history this frame. CameraFrameState.ResolveHistoryReset is the only writer.
    internal enum EHistoryReset
    {
        None,
        // Previews and probe faces never keep history.
        NotTemporal,
        // A capture or validation run holds the camera at frame zero. Only Editor and Development builds produce it.
        Locked,
        // The camera has no committed frame.
        NewCamera,
        // A game camera skipped at least one frame.
        Unseen,
        // The camera's previous frame failed and was rolled back.
        RolledBack,
        // Display size, internal size or super resolution changed.
        Dimensions,
        // The camera switched temporal reconstruction backend or mode.
        BackendChanged,
        // The camera jumped or its projection changed.
        CameraCut,
    }

    // The temporal resolve a camera records, decided once in PrepareCamera from its policy and dimensions.
    internal enum ETemporalResolve
    {
        None,
        TAA,
        SuperResolution,
    }

    internal class CameraFrameState : IDisposable
    {
        public int cameraId;
        public Camera camera;
        public bool preparedThisRender;
        public CameraUniform cameraUniform;
        internal readonly UniformBuffer<ViewUniform> viewUniform = new UniformBuffer<ViewUniform>();
        internal readonly ResourcePool resourcePool;
        public VolumeStack volumeStack;
        public HistoryCache historyCache;
        public FrameFeatureSet features;
        public int lastSeenFrame;
        public CameraType cameraType;
        // Dimensions of the last prepared frame. PrepareCamera resets history when this frame's differ.
        public CameraDimensionDescriptor preparedDimensions;
        internal readonly TextureCapacityState internalCapacity = new TextureCapacityState();
        internal readonly TextureCapacityState displayCapacity = new TextureCapacityState();
        public bool executeSucceeded;
        public bool requiresHistoryReset;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool loggedOutputDecision;
#endif
        public bool hasResolvedBackbufferFormat;
        public GraphicsFormat lastResolvedBackbufferFormat;

        ICameraStateExtension[] m_Extensions = new ICameraStateExtension[8];
        int m_ExtensionHighWater;

        internal T GetOrCreateExtension<T>(CameraExtensionKey<T> key, Func<ResourcePool, T> factory) where T : class, ICameraStateExtension
        {
            int slot = key.slotIndex;
            if (slot >= m_Extensions.Length)
            {
                Array.Resize(ref m_Extensions, math.max(slot + 1, m_Extensions.Length * 2));
            }
            if (m_Extensions[slot] is T existing)
            {
                return existing;
            }
            T created = factory(resourcePool);
            m_Extensions[slot] = created;
            if (slot >= m_ExtensionHighWater)
            {
                m_ExtensionHighWater = slot + 1;
            }
            return created;
        }

        internal bool TryGetExtension<T>(CameraExtensionKey<T> key, out T extension) where T : class, ICameraStateExtension
        {
            int slot = key.slotIndex;
            if ((uint)slot < (uint)m_Extensions.Length && m_Extensions[slot] is T found)
            {
                extension = found;
                return true;
            }
            extension = null;
            return false;
        }

        internal ScreenSpaceCameraState GetScreenSpaceState() => GetOrCreateExtension(ScreenSpaceCameraState.Key, ScreenSpaceCameraState.Factory);
        internal LightingCameraState GetLightingState() => GetOrCreateExtension(LightingCameraState.Key, LightingCameraState.Factory);
        internal AtmosphereCameraState GetAtmosphereState() => GetOrCreateExtension(AtmosphereCameraState.Key, AtmosphereCameraState.Factory);
        internal MeshMotionCameraState GetMeshMotionState() => GetOrCreateExtension(MeshMotionCameraState.Key, MeshMotionCameraState.Factory);
        internal TemporalCameraState GetTemporalState() => GetOrCreateExtension(TemporalCameraState.Key, TemporalCameraState.Factory);

        internal const int GameUnseenFramesToRecycle = 8;
        internal static bool ShouldRecycle(int lastSeenFrame, int frameCount, CameraType cameraType)
        {
            return cameraType != CameraType.SceneView && frameCount - lastSeenFrame > GameUnseenFramesToRecycle;
        }

        // Scene views repaint sparsely; only a game camera that missed a frame has stale history.
        internal static bool SkippedFrame(int previousSeenFrame, int frameCount, CameraType cameraType)
        {
            return cameraType != CameraType.SceneView && frameCount - previousSeenFrame > 1;
        }

        // The only writer of the history-reset decision. previousSeenFrame is lastSeenFrame before this render.
        internal EHistoryReset ResolveHistoryReset(Camera camera, CameraRenderPolicy policy, bool newlyCreated, int previousSeenFrame, int frameCount,
            in CameraDimensionDescriptor dimensions, bool temporalBackendChanged = false)
        {
            if (!policy.temporal)
            {
                return EHistoryReset.NotTemporal;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (PipelineDiagnostics.LocksHistoryReset(camera))
            {
                return EHistoryReset.Locked;
            }
#endif
            if (newlyCreated || !cameraUniform.hasCommittedView)
            {
                return EHistoryReset.NewCamera;
            }
            if (SkippedFrame(previousSeenFrame, frameCount, camera.cameraType))
            {
                return EHistoryReset.Unseen;
            }
            if (requiresHistoryReset)
            {
                return EHistoryReset.RolledBack;
            }
            if (math.any(preparedDimensions.displaySize != dimensions.displaySize) || math.any(preparedDimensions.internalSize != dimensions.internalSize)
                || preparedDimensions.superResolution != dimensions.superResolution)
            {
                return EHistoryReset.Dimensions;
            }
            if (temporalBackendChanged)
            {
                return EHistoryReset.BackendChanged;
            }
            return cameraUniform.IsCut(camera) ? EHistoryReset.CameraCut : EHistoryReset.None;
        }

        public CameraFrameState(int cameraId, ResourcePool pool)
        {
            this.cameraId = cameraId;
            resourcePool = pool;
            cameraUniform = new CameraUniform();
            volumeStack = VolumeManager.instance.CreateStack();
            historyCache = new HistoryCache(pool);
            features = new FrameFeatureSet();
            hasResolvedBackbufferFormat = false;
            lastResolvedBackbufferFormat = GraphicsFormat.None;
            // Register zero-GPU-cost core acceptance extensions upfront so Frame 0 acceptance publishes even before first read.
            _ = GetScreenSpaceState();
            _ = GetTemporalState();
        }

        internal void BeginFrame()
        {
            historyCache?.BeginFrame();
            for (int i = 0; i < m_ExtensionHighWater; ++i)
            {
                m_Extensions[i]?.BeginFrame();
            }
        }

        // This camera threw while recording or executing.
        internal void CameraFailed(bool queueUncertain)
        {
            historyCache?.CameraFailed(queueUncertain);
            for (int i = 0; i < m_ExtensionHighWater; ++i)
            {
                m_Extensions[i]?.CameraFailed(queueUncertain);
            }
            EndFailedFrame();
        }

        // The frame's submission failed after this camera was prepared.
        internal void RollbackFrame()
        {
            historyCache?.RollbackPending();
            for (int i = 0; i < m_ExtensionHighWater; ++i)
            {
                m_Extensions[i]?.RollbackFrame();
            }
            EndFailedFrame();
        }

        void EndFailedFrame()
        {
            preparedThisRender = false;
            executeSucceeded = false;
            requiresHistoryReset = true;
        }

        internal void FlushRetired(ResourceReuseFence fence)
        {
            historyCache?.FlushRetired(fence);
            for (int i = 0; i < m_ExtensionHighWater; ++i)
            {
                m_Extensions[i]?.FlushRetired(fence);
            }
        }

        internal void CommitFrame()
        {
            if (!executeSucceeded)
            {
                return;
            }
            cameraUniform.Commit();
            internalCapacity.Commit();
            displayCapacity.Commit();
            historyCache?.CommitFrame();
            int frameCount = Time.frameCount;
            for (int i = 0; i < m_ExtensionHighWater; ++i)
            {
                m_Extensions[i]?.CommitFrame(features, frameCount);
            }
            preparedThisRender = false;
            executeSucceeded = false;
            requiresHistoryReset = false;
        }

        public void Dispose() => Dispose(default);

        // fence covers the last frame's GPU use when a state is recycled right after its submission.
        internal void Dispose(ResourceReuseFence fence)
        {
            viewUniform.Dispose();
            for (int i = 0; i < m_ExtensionHighWater; ++i)
            {
                if (m_Extensions[i] != null)
                {
                    m_Extensions[i].Dispose(fence);
                    m_Extensions[i] = null;
                }
            }
            m_ExtensionHighWater = 0;

            if (volumeStack != null)
            {
                VolumeManager.instance.DestroyStack(volumeStack);
                volumeStack = null;
            }

            if (historyCache != null)
            {
                historyCache.Dispose(fence);
                historyCache = null;
            }
        }
    }
}
