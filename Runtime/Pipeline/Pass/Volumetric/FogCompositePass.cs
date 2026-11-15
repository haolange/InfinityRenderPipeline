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

    public partial class InfinityRenderPipeline
    {
        struct FogCompositePassData
        {
            public ComputeKernel kernel;
            public LocalKeyword fogKeyword;
            public LocalKeyword cloudKeyword;
            public int2 resolution;
            public RGConstantsHandle parameters;
            public int hasFog;
            public int hasCloud;
            public ComputeShader fogCompositeShader;
            public RGTextureHandle sceneColorTexture;
            public RGTextureHandle foggedSceneColorTexture;
            public RGTextureHandle depthTexture;
            public RGTextureHandle volumetricFogTexture;
            public RGTextureHandle volumetricCloudTexture;
        }

        void ComputeFogComposite(in CameraRecordContext context)
        {
            bool hasFog = context.features.ShouldRecord(EFrameFeature.VolumetricFog);
            bool hasCloud = context.features.ShouldRecord(EFrameFeature.VolumetricCloud);
            RGTextureHandle volumetricFogTexture = hasFog ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.VolumetricFogBuffer) : default;
            RGTextureHandle volumetricCloudTexture = hasCloud ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.VolumetricCloudBuffer) : default;
            if (!TranslucentFeatureUtility.ShouldRecordFogComposite(hasFog, hasCloud))
            {
                // No fog to composite: the opaque scene color is the fogged scene color.
                m_RGBuilder.scoper.MoveTexture(InfinityShaderIDs.OpaqueSceneColorBuffer, InfinityShaderIDs.FoggedSceneColorBuffer);
                m_RGBuilder.scoper.PublishTextureRole(InfinityShaderIDs.ResolvedSceneColor, m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer));
                return;
            }

            RGTextureHandle sceneColorTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.OpaqueSceneColorBuffer);
            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            float maxDistance = 64.0f;
            var volFog = context.volumes.volumetricFog;
            maxDistance = volFog.MaxDistance.value;

            TextureDescriptor foggedDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            foggedDsc.name = "FoggedSceneColor";
            foggedDsc.dimension = TextureDimension.Tex2D;
            foggedDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            foggedDsc.enableRandomWrite = true;
            foggedDsc.filterMode = FilterMode.Bilinear;
            foggedDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureHandle foggedSceneColor = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.FoggedSceneColorBuffer, foggedDsc);

            using (var passRef = m_RGBuilder.AddComputePass<FogCompositePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeFogComposite)))
            {
                ref FogCompositePassData passData = ref passRef.GetPassData();
                passData.kernel = kernels.fogComposite;
                passData.fogKeyword = kernels.fogCompositeFog;
                passData.cloudKeyword = kernels.fogCompositeCloud;
                passData.resolution = new int2(context.dimensions.internalSize.x, context.dimensions.internalSize.y);
                passData.parameters = passRef.WriteConstants(new FogCompositeParameters
                {
                    resolution = new Vector4(context.dimensions.internalSize.x, context.dimensions.internalSize.y, 1.0f / context.dimensions.internalSize.x, 1.0f / context.dimensions.internalSize.y),
                    maxDistance = maxDistance
                });
                passData.hasFog = hasFog ? 1 : 0;
                passData.hasCloud = hasCloud ? 1 : 0;
                passData.fogCompositeShader = shaders.fogCompositeShader;
                passData.sceneColorTexture = passRef.ReadTexture(sceneColorTexture);
                passData.foggedSceneColorTexture = passRef.WriteTexture(foggedSceneColor);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                if (hasFog)
                {
                    passData.volumetricFogTexture = passRef.ReadTexture(volumetricFogTexture);
                }
                if (hasCloud)
                {
                    passData.volumetricCloudTexture = passRef.ReadTexture(volumetricCloudTexture);
                }

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in FogCompositePassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.fogCompositeShader;
                    cmdEncoder.SetComputeConstantBufferParam(shader, FogCompositePassUtilityData.CBV_FogCompositeParametersID, passData.parameters);
                    cmdEncoder.SetKeyword(shader, passData.fogKeyword, passData.hasFog != 0);
                    cmdEncoder.SetKeyword(shader, passData.cloudKeyword, passData.hasCloud != 0);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, PassShaderIDs.SRV_SceneColorTexture, passData.sceneColorTexture);
                    if (passData.hasFog != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, FogCompositePassUtilityData.SRV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    }
                    if (passData.hasCloud != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, FogCompositePassUtilityData.SRV_VolumetricCloudTextureID, passData.volumetricCloudTexture);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, FogCompositePassUtilityData.UAV_FoggedSceneColorTextureID, passData.foggedSceneColorTexture);
                    cmdEncoder.DispatchCompute(shader, passData.kernel.index, passData.kernel.GroupsX(passData.resolution.x), passData.kernel.GroupsY(passData.resolution.y), 1);
                });
            }
            m_RGBuilder.scoper.PublishTextureRole(InfinityShaderIDs.ResolvedSceneColor, foggedSceneColor);
        }
    }
}
