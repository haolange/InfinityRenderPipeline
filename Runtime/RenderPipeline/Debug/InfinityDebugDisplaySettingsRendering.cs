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
        // ReflectionProbeAtlas view: the probe (list order of the last camera) whose six faces are shown.
        public int reflectionProbeAtlasIndex;
        // Light / probe complexity views: the count shown at the hot end of the heat ramp.
        public int complexityRange = DefaultComplexityRange;
        public const int DefaultComplexityRange = 16;
        public int lastReflectionProbeCount;

        public void Reset()
        {
            reflectionProbeAtlasIndex = 0;
            complexityRange = DefaultComplexityRange;
            lastReflectionProbeCount = 0;
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

    public sealed class InfinityDebugDisplaySettingsTimings
    {
        bool m_RecordPassTimings;

        // Turns the ProfilingSampler recorders of every pass on or off (ProfilingSampler.enableRecording).
        public bool recordPassTimings
        {
            get => m_RecordPassTimings;
            set
            {
                if (m_RecordPassTimings == value) return;
                m_RecordPassTimings = value;
                for (int i = 0; i < (int)CustomSamplerId.Max; i++)
                {
                    ProfilingSampler.Get((CustomSamplerId)i).enableRecording = value;
                }
            }
        }

        // "GPU 0.123 ms / CPU 0.045 ms" for one pass; "-" while recording is off or the pass did not run.
        public string Describe(CustomSamplerId id)
        {
            if (!m_RecordPassTimings) return "-";
            ProfilingSampler sampler = ProfilingSampler.Get(id);
            float gpu = sampler.gpuElapsedTime;
            float cpu = sampler.cpuElapsedTime;
            if (gpu <= 0 && cpu <= 0) return "-";
            return $"GPU {gpu:F3} ms / CPU {cpu:F3} ms";
        }

        public string DescribeTotal()
        {
            if (!m_RecordPassTimings) return "-";
            float gpu = 0;
            for (int i = 0; i < (int)CustomSamplerId.Max; i++)
            {
                gpu += ProfilingSampler.Get((CustomSamplerId)i).gpuElapsedTime;
            }
            return $"GPU {gpu:F3} ms";
        }

        public void Reset()
        {
            recordPassTimings = false;
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
