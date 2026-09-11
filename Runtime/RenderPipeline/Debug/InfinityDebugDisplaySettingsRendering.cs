using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public sealed class InfinityDebugDisplaySettingsRendering
    {
        public EDebugView debugView = EDebugView.None;

        public void Reset()
        {
            debugView = EDebugView.None;
        }
    }

    public sealed class InfinityDebugDisplaySettingsTemporal
    {
        public bool showOverlay;
        public bool preferNativeMotionVectors;
        public bool fuseTemporalSharpen;

        public void Reset()
        {
            showOverlay = false;
            preferNativeMotionVectors = false;
            fuseTemporalSharpen = false;
        }
    }

    public sealed class InfinityDebugDisplaySettingsLighting
    {
        public int lastDirectionalCount;
        public int lastLocalCount;
        public ERayTracingBackend rayTracingBackend;

        public void Reset()
        {
            lastDirectionalCount = 0;
            lastLocalCount = 0;
            rayTracingBackend = ERayTracingBackend.Unavailable;
        }
    }

    public sealed class InfinityDebugDisplaySettingsMesh
    {
        public float matrixDuplicateRatio = 1.0f;

        public void Reset()
        {
            matrixDuplicateRatio = 1.0f;
        }
    }
}
