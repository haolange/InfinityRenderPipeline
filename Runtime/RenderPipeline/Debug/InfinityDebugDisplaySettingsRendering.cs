using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public sealed class InfinityDebugDisplaySettingsRendering
    {
        public EDebugView debugView = EDebugView.None;
        // Feature state of the most recently configured camera (read-only display).
        public FrameFeatureSet lastFeatures;
        // Frame Settings of the most recently configured camera.
        public FResolvedFrameSettings lastFrameSettings;
        public bool hasFrameSettings;

        public void Reset()
        {
            debugView = EDebugView.None;
            lastFeatures = null;
            hasFrameSettings = false;
        }

        public string DescribeFrameSetting(EFrameSetting setting)
        {
            if (!hasFrameSettings) return "-";
            return (lastFrameSettings.IsEnabled(setting) ? "On" : "Off") + " (" + lastFrameSettings.SourceOf(setting) + ")";
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
        public int lastDistanceCulledCount;
        // Cached local shadow tiles drawn this frame (copied or re-rendered) and how many were re-rendered.
        public int lastCachedShadowTiles;
        public int lastCachedShadowRenders;
        public ERayTracingBackend rayTracingBackend;
        public string rtaoBlockReason = "URT context is not created.";

        public void Reset()
        {
            lastDirectionalCount = 0;
            lastLocalCount = 0;
            lastUnsupportedShapeCount = 0;
            lastDistanceCulledCount = 0;
            lastCachedShadowTiles = 0;
            lastCachedShadowRenders = 0;
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
