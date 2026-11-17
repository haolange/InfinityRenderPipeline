using System;

namespace InfinityTech.Rendering.Pipeline
{
    /// <summary>
    /// What a pipeline built on <see cref="InfinityRenderPipelineBase"/> records, which features every camera must produce,
    /// and whether its graph may use the async compute queue. The derived pipeline passes it to the base constructor once;
    /// the base reads it for feature support, temporal resolve, resource resolution and the required-producer check.
    /// </summary>
    internal readonly struct PipelineCapabilities
    {
        // The features this pipeline records; every other feature reports that the pipeline does not record it.
        internal readonly EFrameFeature[] features;
        // Every camera produces these; resources that cannot support one fail when they resolve.
        internal readonly EFrameFeature[] requiredProducers;
        // The pipeline lets its graph use the async compute queue where the device supports it.
        internal readonly bool asyncCompute;
        // features as a mask indexed by EFrameFeature, built once.
        internal readonly bool[] featureMask;

        internal PipelineCapabilities(EFrameFeature[] features, EFrameFeature[] requiredProducers, bool asyncCompute)
        {
            if (features == null || features.Length == 0)
                throw new ArgumentException("A pipeline records at least one feature.", nameof(features));
            if (requiredProducers == null || requiredProducers.Length == 0)
                throw new ArgumentException("A pipeline requires at least one producer.", nameof(requiredProducers));
            var mask = new bool[FrameFeatureSet.Count];
            foreach (EFrameFeature feature in features)
            {
                if ((uint)feature >= (uint)mask.Length)
                    throw new ArgumentOutOfRangeException(nameof(features), feature, "Not an EFrameFeature value.");
                mask[(int)feature] = true;
            }
            foreach (EFrameFeature feature in requiredProducers)
            {
                if ((uint)feature >= (uint)mask.Length || !mask[(int)feature])
                    throw new ArgumentException($"Required producer {feature} is not a feature this pipeline records.", nameof(requiredProducers));
            }
            this.features = (EFrameFeature[])features.Clone();
            this.requiredProducers = (EFrameFeature[])requiredProducers.Clone();
            this.asyncCompute = asyncCompute;
            featureMask = mask;
        }

        internal bool Records(EFrameFeature feature)
        {
            return featureMask[(int)feature];
        }
    }
}
