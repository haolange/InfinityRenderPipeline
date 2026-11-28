using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Component;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Temporal;

namespace InfinityTech.Rendering.Pipeline
{
    public readonly struct CameraDimensionDescriptor
    {
        public readonly int2 displaySize;
        public readonly int2 internalSize;
        public readonly int2 internalCapacity, displayCapacity;
        public TextureCapacityContext textureCapacity => new TextureCapacityContext(internalCapacity, displayCapacity);
        public readonly float renderScale;
        public readonly bool upscaling;
        public readonly Rect outputViewport;
        public bool downscaled => math.any(internalSize != displaySize);

        internal CameraDimensionDescriptor(in TemporalFrameRequirements requirements, Rect outputViewport)
        {
            internalCapacity = displayCapacity = int2.zero;
            displaySize = math.max(1, requirements.displaySize);
            upscaling = requirements.isUpscaling;
            renderScale = upscaling ? math.clamp(requirements.renderScale, 0.25f, 1.0f) : 1.0f;
            internalSize = upscaling ? math.max(1, requirements.renderSize) : displaySize;
            this.outputViewport = outputViewport;
        }

        public CameraDimensionDescriptor(int2 displaySize, float renderScale, bool upscaling, Rect outputViewport)
        {
            internalCapacity = displayCapacity = int2.zero;
            this.displaySize = new int2(math.max(1, displaySize.x), math.max(1, displaySize.y));
            this.upscaling = upscaling;
            this.renderScale = upscaling ? math.clamp(renderScale, 0.25f, 1.0f) : 1.0f;
            internalSize = this.upscaling
                ? new int2(math.max(1, (int)math.ceil(this.displaySize.x * this.renderScale)), math.max(1, (int)math.ceil(this.displaySize.y * this.renderScale)))
                : this.displaySize;
            this.outputViewport = outputViewport;
        }

        CameraDimensionDescriptor(in CameraDimensionDescriptor source, int2 internalCapacity, int2 displayCapacity)
        {
            displaySize = source.displaySize;
            internalSize = source.internalSize;
            renderScale = source.renderScale;
            upscaling = source.upscaling;
            outputViewport = source.outputViewport;
            this.internalCapacity = internalCapacity;
            this.displayCapacity = displayCapacity;
        }

        internal CameraDimensionDescriptor WithCapacity(int2 internalCapacity, int2 displayCapacity)
            => new CameraDimensionDescriptor(this, internalCapacity, displayCapacity);

        public static CameraDimensionDescriptor FromCamera(Camera camera, bool upscaling, float renderScale, InfinityAdditionalCameraData additional)
        {
            bool enableUpscaling = upscaling && camera.cameraType == CameraType.Game;
            if (additional != null && additional.temporalOverride != ETemporalCameraOverride.FollowPipeline)
            {
                enableUpscaling = false;
            }
            return new CameraDimensionDescriptor(new int2(camera.pixelWidth, camera.pixelHeight), renderScale, enableUpscaling, camera.pixelRect);
        }
    }
}
