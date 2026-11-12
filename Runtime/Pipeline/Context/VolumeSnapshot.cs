using System;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Volume;

namespace InfinityTech.Rendering.Pipeline
{
    // The camera's blended Volume components, read once from its stack after VolumeManager.Update. The pipeline
    // constructor rejects an incomplete registry, so a missing component here is a broken stack and throws.
    internal readonly struct VolumeSnapshot
    {
        internal readonly FilmTonemap filmTonemap;
        internal readonly ColorGrading colorGrading;
        internal readonly Exposure exposure;
        internal readonly Bloom bloom;
        internal readonly Vignette vignette;
        internal readonly FilmGrain filmGrain;
        internal readonly SubsurfaceScattering subsurfaceScattering;
        internal readonly RayTracingAmbientOcclusion rayTracingAmbientOcclusion;
        internal readonly ScreenSpaceAmbientOcclusion screenSpaceAmbientOcclusion;
        internal readonly ScreenSpaceReflection screenSpaceReflection;
        internal readonly ScreenSpaceIndirectDiffuse screenSpaceIndirectDiffuse;
        internal readonly VolumetricFog volumetricFog;
        internal readonly VolumetricCloud volumetricCloud;
        internal readonly ContactShadow contactShadow;

        VolumeSnapshot(VolumeStack stack)
        {
            filmTonemap = Require<FilmTonemap>(stack);
            colorGrading = Require<ColorGrading>(stack);
            exposure = Require<Exposure>(stack);
            bloom = Require<Bloom>(stack);
            vignette = Require<Vignette>(stack);
            filmGrain = Require<FilmGrain>(stack);
            subsurfaceScattering = Require<SubsurfaceScattering>(stack);
            rayTracingAmbientOcclusion = Require<RayTracingAmbientOcclusion>(stack);
            screenSpaceAmbientOcclusion = Require<ScreenSpaceAmbientOcclusion>(stack);
            screenSpaceReflection = Require<ScreenSpaceReflection>(stack);
            screenSpaceIndirectDiffuse = Require<ScreenSpaceIndirectDiffuse>(stack);
            volumetricFog = Require<VolumetricFog>(stack);
            volumetricCloud = Require<VolumetricCloud>(stack);
            contactShadow = Require<ContactShadow>(stack);
        }

        internal static VolumeSnapshot Capture(VolumeStack stack) => new VolumeSnapshot(stack);

        static T Require<T>(VolumeStack stack) where T : VolumeComponent
        {
            return stack.GetComponent<T>() ?? throw new InvalidOperationException(
                "The camera Volume stack has no " + typeof(T).Name + "; the default Volume profile must contain every consumed component.");
        }
    }
}
