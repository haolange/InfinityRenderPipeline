using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    public enum ECameraRenderClass
    {
        // Game and SceneView: jitter, temporal history, post-processing and display encoding.
        Full = 0,
        // Material/asset previews: one-shot frames. No jitter or history; post and display encoding stay.
        Preview = 1,
        // Reflection-probe faces: HDR scene-linear radiance. No jitter, history, exposure, tonemapping or encoding.
        Capture = 2,
    }

    // What a camera type is allowed to record. Feature gating and frame setup read this policy instead of
    // testing CameraType in individual passes.
    public readonly struct CameraRenderPolicy
    {
        public readonly ECameraRenderClass renderClass;

        public CameraRenderPolicy(ECameraRenderClass renderClass)
        {
            this.renderClass = renderClass;
        }

        // Jitter, TAA/SR and every feature that accumulates history (SSR, SSGI, GTAO, volumetric fog/cloud).
        public bool temporal => renderClass == ECameraRenderClass.Full;

        // Exposure, bloom, ACES/grading LUT, vignette and grain. Capture cameras store radiance, not display values.
        public bool postProcessing => renderClass != ECameraRenderClass.Capture;

        public static CameraRenderPolicy For(CameraType cameraType)
        {
            switch (cameraType)
            {
                case CameraType.Reflection:
                    return new CameraRenderPolicy(ECameraRenderClass.Capture);
                case CameraType.Preview:
                    return new CameraRenderPolicy(ECameraRenderClass.Preview);
                default:
                    return new CameraRenderPolicy(ECameraRenderClass.Full);
            }
        }
    }
}
