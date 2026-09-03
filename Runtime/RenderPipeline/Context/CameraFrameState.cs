using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal sealed class AtmosphereViewCache
    {
    }

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
        public int pixelWidth;
        public int pixelHeight;
        public GraphicsFormat colorFormat;
        public AtmosphereViewCache atmosphereViewCache;
        public ExposureState exposureState;
        public bool executeSucceeded;

        public CameraFrameState(int cameraId)
        {
            this.cameraId = cameraId;
            cameraUniform = new CameraUniform();
            volumeStack = VolumeManager.instance.CreateStack();
            historyCache = new HistoryCache();
            features = new FrameFeatureSet();
            atmosphereViewCache = new AtmosphereViewCache();
            exposureState = new ExposureState { evCompensation = 0.0f };
            colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
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
        }
    }
}
