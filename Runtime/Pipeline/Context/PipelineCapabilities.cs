using System;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    /// <summary>
    /// What a pipeline built on <see cref="InfinityRenderPipelineBase"/> records, which features every camera must produce,
    /// whether its graph may use the async compute queue, and the mesh passes it draws. The derived pipeline passes it to the
    /// base constructor once; the base reads it for feature support, temporal resolve, resource resolution and the
    /// required-producer check, and hands the mesh pass table to the scene world.
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
        // The mesh pass table (R1: data of the pipeline); the mesh world builds one pass cache per entry and the pipeline's
        // passes name entries by MeshPassId.
        internal readonly MeshPassDesc[] meshPasses;
        // The entry of meshPasses whose members cast shadows.
        internal readonly MeshPassId shadowCasterPass;

        internal PipelineCapabilities(EFrameFeature[] features, EFrameFeature[] requiredProducers, bool asyncCompute,
            MeshPassDesc[] meshPasses, MeshPassId shadowCasterPass)
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
            if (meshPasses == null || meshPasses.Length == 0)
                throw new ArgumentException("A pipeline registers at least one mesh pass.", nameof(meshPasses));
            if ((uint)shadowCasterPass.index >= (uint)meshPasses.Length)
                throw new ArgumentOutOfRangeException(nameof(shadowCasterPass), shadowCasterPass.index, "The shadow caster pass is not an entry of the mesh pass table.");
            this.features = (EFrameFeature[])features.Clone();
            this.requiredProducers = (EFrameFeature[])requiredProducers.Clone();
            this.asyncCompute = asyncCompute;
            featureMask = mask;
            this.meshPasses = (MeshPassDesc[])meshPasses.Clone();
            this.shadowCasterPass = shadowCasterPass;
        }

        internal bool Records(EFrameFeature feature)
        {
            return featureMask[(int)feature];
        }
    }
}
