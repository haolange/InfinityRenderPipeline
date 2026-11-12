using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FogCompositePassUtilityData
    {
        internal static readonly int FogComposite_ResolutionID = Shader.PropertyToID("FogComposite_Resolution");
        internal static readonly int FogComposite_MaxDistanceID = Shader.PropertyToID("FogComposite_MaxDistance");
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
            public float maxDistance;
            public int hasFog;
            public int hasCloud;
            public Matrix4x4 matrix_InvViewProj;
            public Vector4 worldSpaceCameraPos;
            public ComputeShader fogCompositeShader;
            public RGTextureHandle sceneColorTexture;
            public RGTextureHandle foggedSceneColorTexture;
            public RGTextureHandle depthTexture;
            public RGTextureHandle volumetricFogTexture;
            public RGTextureHandle volumetricCloudTexture;
        }

        void ComputeFogComposite(in CameraRecordContext context)
        {
            bool hasFog = m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.VolumetricFogBuffer, out RGTextureHandle volumetricFogTexture);
            bool hasCloud = m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.VolumetricCloudBuffer, out RGTextureHandle volumetricCloudTexture);
            if (!TranslucentFeatureUtility.ShouldRecordFogComposite(hasFog, hasCloud))
            {
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
                passData.maxDistance = maxDistance;
                passData.hasFog = hasFog ? 1 : 0;
                passData.hasCloud = hasCloud ? 1 : 0;
                passData.matrix_InvViewProj = context.cameraUniform.matrix_InvViewFlipYJitterProj;
                passData.worldSpaceCameraPos = context.camera.transform.position;
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
                    cmdEncoder.SetComputeVectorParam(shader, FogCompositePassUtilityData.FogComposite_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeFloatParam(shader, FogCompositePassUtilityData.FogComposite_MaxDistanceID, passData.maxDistance);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_InvViewProj, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(shader, PassShaderIDs.WorldSpaceCameraPos, passData.worldSpaceCameraPos);
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
        }

        void ResolveFoggedSceneColor()
        {
            if (!m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer, out _))
            {
                m_RGBuilder.scoper.MoveTexture(InfinityShaderIDs.OpaqueSceneColorBuffer, InfinityShaderIDs.FoggedSceneColorBuffer);
            }
            m_RGBuilder.scoper.PublishTextureRole(InfinityShaderIDs.ResolvedSceneColor, m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer));
        }
    }
}
