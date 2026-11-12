using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;

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
        internal readonly MeshViewMotionHistory meshMotionHistory = new MeshViewMotionHistory();
        public VolumeStack volumeStack;
        public HistoryCache historyCache;
        public FrameFeatureSet features;
        public int lastSeenFrame;
        public CameraType cameraType;
        // Dimensions of the last prepared frame. PrepareCamera resets history when this frame's differ.
        public CameraDimensionDescriptor preparedDimensions;
        internal readonly TextureCapacityState internalCapacity = new TextureCapacityState();
        internal readonly TextureCapacityState displayCapacity = new TextureCapacityState();
        public AtmosphereViewCache atmosphereViewCache;
        public CombineLutCache combineLutCache;
        public bool executeSucceeded;
        public bool requiresHistoryReset;
        public bool loggedOutputDecision;
        public bool hasResolvedBackbufferFormat;
        public GraphicsFormat lastResolvedBackbufferFormat;
        internal bool acceptedHiZ;
        internal bool acceptedSSR, acceptedSSGI, acceptedGTAO;
        internal bool acceptedTemporalColor, acceptedTemporalColorIsSR;
        internal TemporalColorView temporalColorView;
        internal ulong meshViewKey;

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
            in CameraDimensionDescriptor dimensions)
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
            return cameraUniform.IsCut(camera) ? EHistoryReset.CameraCut : EHistoryReset.None;
        }

        // Camera-owned frame transactions, driven in one order.
        readonly IFrameTransaction[] m_Transactions;

        public CameraFrameState(int cameraId, ResourcePool pool)
        {
            this.cameraId = cameraId;
            cameraUniform = new CameraUniform();
            volumeStack = VolumeManager.instance.CreateStack();
            historyCache = new HistoryCache(pool);
            features = new FrameFeatureSet();
            atmosphereViewCache = new AtmosphereViewCache(pool);
            combineLutCache = new CombineLutCache(pool);
            m_Transactions = new IFrameTransaction[] { historyCache, atmosphereViewCache, combineLutCache };
            loggedOutputDecision = false;
            hasResolvedBackbufferFormat = false;
            lastResolvedBackbufferFormat = GraphicsFormat.None;
        }

        internal void BeginFrame()
        {
            for (int i = 0; i < m_Transactions.Length; ++i)
            {
                m_Transactions[i].BeginFrame();
            }
        }

        // This camera threw while recording or executing.
        internal void CameraFailed(bool queueUncertain)
        {
            for (int i = 0; i < m_Transactions.Length; ++i)
            {
                m_Transactions[i].CameraFailed(queueUncertain);
            }
            EndFailedFrame();
        }

        // The frame's submission failed after this camera was prepared.
        internal void RollbackFrame()
        {
            for (int i = 0; i < m_Transactions.Length; ++i)
            {
                m_Transactions[i].RollbackPending();
            }
            EndFailedFrame();
        }

        void EndFailedFrame()
        {
            preparedThisRender = false;
            meshMotionHistory.Rollback();
            executeSucceeded = false;
            requiresHistoryReset = true;
        }

        internal void FlushRetired(ResourceReuseFence fence)
        {
            for (int i = 0; i < m_Transactions.Length; ++i)
            {
                m_Transactions[i].FlushRetired(fence);
            }
        }

        internal bool subsurfaceEnabled;
        internal int acceptedSSGIFrameIndex;

        internal void CommitFrame()
        {
            if (!executeSucceeded)
            {
                return;
            }
            cameraUniform.Commit();
            internalCapacity.Commit();
            displayCapacity.Commit();
            meshMotionHistory.Commit();
            if (features != null && features.IsProduced(EFrameFeature.SSGI))
            {
                acceptedSSGIFrameIndex = Time.frameCount;
            }
            acceptedHiZ = features != null && features.IsProduced(EFrameFeature.HiZ);
            acceptedSSR = features != null && features.IsProduced(EFrameFeature.SSR);
            acceptedSSGI = features != null && features.IsProduced(EFrameFeature.SSGI);
            acceptedGTAO = features != null && features.IsProduced(EFrameFeature.GTAO);
            acceptedTemporalColorIsSR = features != null && features.IsProduced(EFrameFeature.SuperResolution);
            acceptedTemporalColor = acceptedTemporalColorIsSR || features != null && features.IsProduced(EFrameFeature.TAA);
            preparedThisRender = false;
            for (int i = 0; i < m_Transactions.Length; ++i)
            {
                m_Transactions[i].CommitFrame();
            }
            executeSucceeded = false;
            requiresHistoryReset = false;
        }

        public void Dispose() => Dispose(default);

        // fence covers the last frame's GPU use when a state is recycled right after its submission.
        internal void Dispose(ResourceReuseFence fence)
        {
            meshMotionHistory.Clear();
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
    }
}
