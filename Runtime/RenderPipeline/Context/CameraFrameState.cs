using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal struct ExposureState
    {
        public float evCompensation;
    }

    internal class CameraFrameState : IDisposable
    {
        public int cameraId;
        public CameraUniform cameraUniform;
        public VolumeStack volumeStack;
        public HistoryCache historyCache;
        public FrameFeatureSet features;
        public int descriptorGeneration;
        public int lastSeenFrame;
        public CameraType cameraType;
        public int pixelWidth;
        public int pixelHeight;
        public GraphicsFormat colorFormat;
        public AtmosphereViewCache atmosphereViewCache;
        public CombineLutCache combineLutCache;
        public ExposureState exposureState;
        public bool executeSucceeded;
        public bool loggedOutputDecision;
        public bool hasResolvedBackbufferFormat;
        public GraphicsFormat lastResolvedBackbufferFormat;
        public int ssrValidFrames;
        public int ssgiValidFrames;
        public int gtaoValidFrames;
        public int taaValidFrames;

        internal const int GameUnseenFramesToRecycle = 8;
        internal const int SceneViewUnseenFramesToRecycle = 120;

        internal static int UnseenFramesToRecycle(CameraType cameraType)
        {
            return cameraType == CameraType.SceneView ? SceneViewUnseenFramesToRecycle : GameUnseenFramesToRecycle;
        }

        internal static bool ShouldRecycle(int lastSeenFrame, int frameCount, CameraType cameraType)
        {
            return frameCount - lastSeenFrame > UnseenFramesToRecycle(cameraType);
        }

        internal static bool ShouldForceHistoryReset(bool newlyCreated, int lastSeenFrame, int frameCount)
        {
            return newlyCreated || (frameCount - lastSeenFrame > 1);
        }

        public CameraFrameState(int cameraId)
        {
            this.cameraId = cameraId;
            cameraUniform = new CameraUniform();
            volumeStack = VolumeManager.instance.CreateStack();
            historyCache = new HistoryCache();
            features = new FrameFeatureSet();
            atmosphereViewCache = new AtmosphereViewCache();
            combineLutCache = new CombineLutCache();
            exposureState = new ExposureState { evCompensation = 0.0f };
            colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
            loggedOutputDecision = false;
            hasResolvedBackbufferFormat = false;
            lastResolvedBackbufferFormat = GraphicsFormat.None;
        }

        public void Dispose()
        {
            if (volumeStack != null)
            {
                VolumeManager.instance.DestroyStack(volumeStack);
                volumeStack = null;
            }

            if (historyCache != null)
            {
                historyCache.Release();
                historyCache.ForceFlushForTeardown();
                historyCache = null;
            }

            if (atmosphereViewCache != null)
            {
                atmosphereViewCache.Dispose();
                atmosphereViewCache = null;
            }

            if (combineLutCache != null)
            {
                combineLutCache.Dispose();
                combineLutCache = null;
            }
        }
    }
}
