using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace InfinityTech.Rendering
{
    public enum ESSSQuality
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FDiffusionProfileRecord
    {
        public Vector4 scatterAlbedoDistance;
        public Vector4 parameters;
    }

    [CreateAssetMenu(menuName = "InfinityRenderPipeline/DiffusionProfile", order = 361)]
    public sealed class DiffusionProfile : ScriptableObject
    {
        public Color scatterColor = new Color(1.0f, 0.2f, 0.1f, 1.0f);
        public float scatterDistance = 1.0f;
        public Color surfaceAlbedo = new Color(0.8f, 0.4f, 0.3f, 1.0f);
        public float maxRadius = 5.0f;

        public FDiffusionProfileRecord ToRecord()
        {
            FDiffusionProfileRecord record;
            Color albedo = scatterColor * surfaceAlbedo;
            record.scatterAlbedoDistance = new Vector4(albedo.r, albedo.g, albedo.b, scatterDistance);
            record.parameters = new Vector4(maxRadius, 0.0f, 0.0f, 0.0f);
            return record;
        }

        public static int SampleCount(ESSSQuality quality)
        {
            switch (quality)
            {
                case ESSSQuality.Low:
                    return 7;
                case ESSSQuality.High:
                    return 17;
                default:
                    return 11;
            }
        }
    }
}
