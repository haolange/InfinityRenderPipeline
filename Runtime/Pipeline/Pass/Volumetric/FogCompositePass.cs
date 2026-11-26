using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_FogCompositeParameters Shaders/Pass/Volumetric/Compute_FogComposite.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct FogCompositeParameters
    {
        public Vector4 resolution;
        public float maxDistance;
        public Vector3 padding0;
    }

    internal static class FogCompositePassUtilityData
    {
        internal static readonly int CBV_FogCompositeParametersID = Shader.PropertyToID("CBV_FogCompositeParameters");
        internal static readonly int SRV_VolumetricFogTextureID = Shader.PropertyToID("SRV_VolumetricFogTexture");
        internal static readonly int SRV_VolumetricCloudTextureID = Shader.PropertyToID("SRV_VolumetricCloudTexture");
        internal static readonly int UAV_FoggedSceneColorTextureID = Shader.PropertyToID("UAV_FoggedSceneColorTexture");
    }

    internal sealed class FogCompositePass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;

        internal FogCompositePass(RGBuilder graph, InfinityRenderPipelineResources resources, in InfinityRenderPipelineAsset.Settings settings)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
            m_Settings = settings;
        }

        struct FogCompositePassData
        {
            public ComputeKernel kernel;
            public LocalKeyword fogKeyword;
            public LocalKeyword cloudKeyword;
            public int2 resolution;
            public RGConstantsHandle parametersHandle;
            public int hasFog;
            public int hasCloud;
            public ComputeShader fogCompositeShader;
            public RGTextureHandle sceneColorTextureHandle;
            public RGTextureHandle foggedSceneColorTextureHandle;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle volumetricFogTextureHandle;
            public RGTextureHandle volumetricCloudTextureHandle;
        }

        internal void ComputeFogComposite(in ViewContext context)
        {
            bool hasFog = context.features.ShouldRecord(EFrameFeature.VolumetricFog);
            bool hasCloud = context.features.ShouldRecord(EFrameFeature.VolumetricCloud);
            RGTextureHandle volumetricFogTextureHandle = hasFog ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.VolumetricFogBuffer) : default;
            RGTextureHandle volumetricCloudTextureHandle = hasCloud ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.VolumetricCloudBuffer) : default;
            // Without fog or clouds the opaque scene color is the fogged one (SceneColorKeys.fogged).
            if (!hasFog && !hasCloud)
            {
                return;
            }

            RGTextureHandle sceneColorTextureHandle = m_RGBuilder.scoper.QueryTexture(context.sceneColor.opaque);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            float maxDistance = 64.0f;
            var volFog = context.volumes.volumetricFog;
            maxDistance = volFog.MaxDistance.value;

            TextureDescriptor foggedDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            foggedDsc.name = "FoggedSceneColor";
            foggedDsc.dimension = TextureDimension.Tex2D;
            foggedDsc.colorFormat = EffectivePipelineConfiguration.SceneColorFormat;
            foggedDsc.enableRandomWrite = true;
            foggedDsc.filterMode = FilterMode.Bilinear;
            foggedDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureHandle foggedSceneColorHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.FoggedSceneColorBuffer, foggedDsc);

            {
                RGComputePassHandle<FogCompositePassData> passHandle = m_RGBuilder.AddComputePass<FogCompositePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeFogComposite));
                ref FogCompositePassData passData = ref passHandle.GetPassData();
                passData.kernel = m_Resources.kernels.fogComposite;
                passData.fogKeyword = m_Resources.kernels.fogCompositeFog;
                passData.cloudKeyword = m_Resources.kernels.fogCompositeCloud;
                passData.resolution = new int2(context.dimensions.internalSize.x, context.dimensions.internalSize.y);
                passData.parametersHandle = passHandle.WriteConstants(new FogCompositeParameters
                {
                    resolution = new Vector4(context.dimensions.internalSize.x, context.dimensions.internalSize.y, 1.0f / context.dimensions.internalSize.x, 1.0f / context.dimensions.internalSize.y),
                    maxDistance = maxDistance
                });
                passData.hasFog = hasFog ? 1 : 0;
                passData.hasCloud = hasCloud ? 1 : 0;
                passData.fogCompositeShader = m_Resources.shaders.fogCompositeShader;
                passData.sceneColorTextureHandle = passHandle.ReadTexture(sceneColorTextureHandle);
                passData.foggedSceneColorTextureHandle = passHandle.WriteTexture(foggedSceneColorHandle);
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                if (hasFog)
                {
                    passData.volumetricFogTextureHandle = passHandle.ReadTexture(volumetricFogTextureHandle);
                }
                if (hasCloud)
                {
                    passData.volumetricCloudTextureHandle = passHandle.ReadTexture(volumetricCloudTextureHandle);
                }

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in FogCompositePassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.fogCompositeShader;
                    cmdEncoder.SetComputeConstantBufferParam(shader, FogCompositePassUtilityData.CBV_FogCompositeParametersID, passData.parametersHandle);
                    cmdEncoder.SetKeyword(shader, passData.fogKeyword, passData.hasFog != 0);
                    cmdEncoder.SetKeyword(shader, passData.cloudKeyword, passData.hasCloud != 0);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, PassShaderIDs.SRV_SceneColorTexture, passData.sceneColorTextureHandle);
                    if (passData.hasFog != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, FogCompositePassUtilityData.SRV_VolumetricFogTextureID, passData.volumetricFogTextureHandle);
                    }
                    if (passData.hasCloud != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, FogCompositePassUtilityData.SRV_VolumetricCloudTextureID, passData.volumetricCloudTextureHandle);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, FogCompositePassUtilityData.UAV_FoggedSceneColorTextureID, passData.foggedSceneColorTextureHandle);
                    cmdEncoder.DispatchCompute(shader, passData.kernel.index, passData.kernel.GroupsX(passData.resolution.x), passData.kernel.GroupsY(passData.resolution.y), 1);
                });
            }
        }
    }
}
