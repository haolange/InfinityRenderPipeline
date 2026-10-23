using System;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Component;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    public enum ESuperResolutionOverride
    {
        UseAsset = 0,
        Off = 1
    }

    public readonly struct CameraDimensionDescriptor
    {
        public readonly int2 displaySize;
        public readonly int2 internalSize;
        public readonly int2 internalCapacity, displayCapacity;
        public FTextureCapacityContext textureCapacity => new FTextureCapacityContext(internalCapacity, displayCapacity);
        public readonly float renderScale;
        public readonly bool superResolution;
        public readonly Rect outputViewport;
        public bool downscaled => math.any(internalSize != displaySize);

        public CameraDimensionDescriptor(int2 displaySize, float renderScale, bool superResolution, Rect outputViewport)
        {
            internalCapacity = displayCapacity = int2.zero;
            this.displaySize = new int2(math.max(1, displaySize.x), math.max(1, displaySize.y));
            this.renderScale = superResolution ? math.clamp(renderScale, 0.5f, 1.0f) : 1.0f;
            this.superResolution = superResolution;
            this.outputViewport = outputViewport;
            if (this.superResolution)
            {
                internalSize = new int2(
                    math.max(1, (int)math.ceil(this.displaySize.x * this.renderScale)),
                    math.max(1, (int)math.ceil(this.displaySize.y * this.renderScale)));
            }
            else
            {
                internalSize = this.displaySize;
            }
        }

        CameraDimensionDescriptor(in CameraDimensionDescriptor source, int2 internalCapacity, int2 displayCapacity)
        {
            displaySize = source.displaySize; internalSize = source.internalSize;
            renderScale = source.renderScale; superResolution = source.superResolution; outputViewport = source.outputViewport;
            this.internalCapacity = internalCapacity; this.displayCapacity = displayCapacity;
        }
        internal CameraDimensionDescriptor WithCapacity(int2 internalCapacity, int2 displayCapacity)
            => new CameraDimensionDescriptor(this, internalCapacity, displayCapacity);

        public static CameraDimensionDescriptor FromCamera(Camera camera, InfinityRenderPipelineAsset asset, InfinityAdditionalCameraData additional)
        {
            bool srEnabled = asset != null && asset.enableSuperResolution;
            if (additional != null && additional.superResolutionOverride == ESuperResolutionOverride.Off)
            {
                srEnabled = false;
            }

            if (camera.cameraType != CameraType.Game)
            {
                srEnabled = false;
            }

            float scale = asset != null ? asset.renderScale : 1.0f;
            return new CameraDimensionDescriptor(new int2(camera.pixelWidth, camera.pixelHeight), scale, srEnabled, camera.pixelRect);
        }
    }
}
