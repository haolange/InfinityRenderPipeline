using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public sealed class InfinityRenderPipelineResources
    {
        public InfinityRenderPipelineRuntimeShaders shaders { get; }
        public InfinityRenderPipelineRuntimeTextures textures { get; }
        public InfinityRenderPipelineRuntimeMaterials materials { get; }
        public VolumeProfile defaultVolumeProfile { get; }

        public InfinityRenderPipelineResources()
        {
            InfinityRenderPipelineGlobalSettings.Require();

            if (!GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeShaders resolvedShaders) || resolvedShaders == null)
                throw new InvalidOperationException("InfinityRP: RuntimeShaders are not registered on GlobalSettings.");
            if (!GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeTextures resolvedTextures) || resolvedTextures == null)
                throw new InvalidOperationException("InfinityRP: RuntimeTextures are not registered on GlobalSettings.");
            if (!GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeMaterials resolvedMaterials) || resolvedMaterials == null)
                throw new InvalidOperationException("InfinityRP: RuntimeMaterials are not registered on GlobalSettings.");

            shaders = resolvedShaders;
            textures = resolvedTextures;
            materials = resolvedMaterials;
            defaultVolumeProfile = InfinityRenderPipelineGlobalSettings.ResolveDefaultVolumeProfile();
            if (textures.bestFitNormalTexture == null)
                throw new InvalidOperationException("InfinityRP: GlobalSettings RuntimeTextures.bestFitNormalTexture is required.");
            if (materials.blitMaterial == null)
                throw new InvalidOperationException("InfinityRP: GlobalSettings RuntimeMaterials.blitMaterial is required.");
            if (!DefaultVolumeProfileFactory.HasRequiredDefaultComponents(defaultVolumeProfile))
                throw new InvalidOperationException("InfinityRP: default Volume profile is incomplete or its required component overrides are disabled.");
        }
    }
}
