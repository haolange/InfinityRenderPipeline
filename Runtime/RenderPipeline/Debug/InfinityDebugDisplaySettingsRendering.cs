using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public sealed class InfinityDebugDisplaySettingsRendering
    {
        public EDebugView debugView = EDebugView.None;
        // Feature state of the most recently configured camera (read-only display).
        public FrameFeatureSet lastFeatures;

        public void Reset()
        {
            debugView = EDebugView.None;
            lastFeatures = null;
        }

        public string DescribeFeature(EFrameFeature feature)
        {
            FrameFeatureSet features = lastFeatures;
            if (features == null) return "-";
            if (!features.IsRequested(feature)) return "Off";
            if (features.IsSupported(feature)) return "Records";
            return "Blocked: " + features.GetUnsupportedReason(feature);
        }
    }

    public sealed class InfinityDebugDisplaySettingsLighting
    {
        public int lastDirectionalCount;
        public int lastLocalCount;
        public int lastUnsupportedShapeCount;
        public ERayTracingBackend rayTracingBackend;
        public string rtaoBlockReason = "URT context is not created.";

        public void Reset()
        {
            lastDirectionalCount = 0;
            lastLocalCount = 0;
            lastUnsupportedShapeCount = 0;
            rayTracingBackend = ERayTracingBackend.Unavailable;
            rtaoBlockReason = "URT context is not created.";
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
