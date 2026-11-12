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
        internal static readonly int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static readonly int SRV_SceneColorTextureID = Shader.PropertyToID("SRV_SceneColorTexture");
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

        void ComputeFogComposite(RenderContext renderContext, Camera camera)
        {
            bool hasFog = m_RGScoper.TryQueryTexture(InfinityShaderIDs.VolumetricFogBuffer, out RGTextureHandle volumetricFogTexture);
            bool hasCloud = m_RGScoper.TryQueryTexture(InfinityShaderIDs.VolumetricCloudBuffer, out RGTextureHandle volumetricCloudTexture);
            if (!TranslucentFeatureUtility.ShouldRecordFogComposite(hasFog, hasCloud))
            {
                return;
            }

            RGTextureHandle sceneColorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.OpaqueSceneColorBuffer);
            RGTextureHandle depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            float maxDistance = 64.0f;
            var volFog = ActiveVolumeStack.GetComponent<InfinityTech.Rendering.Volume.VolumetricFog>();
            maxDistance = volFog.MaxDistance.value;

            TextureDescriptor foggedDsc = new TextureDescriptor(m_ActiveFrameState.dimensions.internalSize.x, m_ActiveFrameState.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            foggedDsc.name = "FoggedSceneColor";
            foggedDsc.dimension = TextureDimension.Tex2D;
            foggedDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            foggedDsc.depthBufferBits = EDepthBits.None;
            foggedDsc.enableRandomWrite = true;
            foggedDsc.filterMode = FilterMode.Bilinear;
            foggedDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureHandle foggedSceneColor = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.FoggedSceneColorBuffer, foggedDsc);

            using (RGComputePassHandle passRef = m_RGBuilder.AddComputePass<FogCompositePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeFogComposite)))
            {
                ref FogCompositePassData passData = ref passRef.GetPassData<FogCompositePassData>();
                passData.kernel = kernels.fogComposite;
                passData.fogKeyword = kernels.fogCompositeFog;
                passData.cloudKeyword = kernels.fogCompositeCloud;
                passData.resolution = new int2(m_ActiveFrameState.dimensions.internalSize.x, m_ActiveFrameState.dimensions.internalSize.y);
                passData.maxDistance = maxDistance;
                passData.hasFog = hasFog ? 1 : 0;
                passData.hasCloud = hasCloud ? 1 : 0;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.worldSpaceCameraPos = camera.transform.position;
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
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(shader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);
                    shader.SetKeyword(passData.fogKeyword, passData.hasFog != 0);
                    shader.SetKeyword(passData.cloudKeyword, passData.hasCloud != 0);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, FogCompositePassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel.index, FogCompositePassUtilityData.SRV_SceneColorTextureID, passData.sceneColorTexture);
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
            if (m_RGScoper.TryQueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer, out _))
            {
                return;
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.OpaqueSceneColorBuffer, out _))
            {
                throw new System.InvalidOperationException("InfinityRP: FoggedSceneColor has no OpaqueSceneColor to adopt.");
            }

            m_RGScoper.MoveTexture(InfinityShaderIDs.OpaqueSceneColorBuffer, InfinityShaderIDs.FoggedSceneColorBuffer);
        }
    }
}
