using System;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Component;

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
        public readonly float renderScale;
        public readonly bool superResolution;

        public CameraDimensionDescriptor(int2 displaySize, float renderScale, bool superResolution)
        {
            this.displaySize = new int2(math.max(1, displaySize.x), math.max(1, displaySize.y));
            this.renderScale = math.clamp(renderScale, 0.5f, 1.0f);
            this.superResolution = superResolution && this.renderScale < 0.999f;
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
            return new CameraDimensionDescriptor(new int2(camera.pixelWidth, camera.pixelHeight), scale, srEnabled);
        }
    }
}
