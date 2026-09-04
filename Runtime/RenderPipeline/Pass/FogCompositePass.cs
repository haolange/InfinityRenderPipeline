using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FogCompositePassUtilityData
    {
        internal static string ReactiveMaskName = "ReactiveMask";
        internal static int FogComposite_ResolutionID = Shader.PropertyToID("FogComposite_Resolution");
        internal static int FogComposite_MaxDistanceID = Shader.PropertyToID("FogComposite_MaxDistance");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_SceneColorTextureID = Shader.PropertyToID("SRV_SceneColorTexture");
        internal static int SRV_VolumetricFogTextureID = Shader.PropertyToID("SRV_VolumetricFogTexture");
        internal static int SRV_VolumetricCloudTextureID = Shader.PropertyToID("SRV_VolumetricCloudTexture");
        internal static int UAV_FoggedSceneColorTextureID = Shader.PropertyToID("UAV_FoggedSceneColorTexture");
        internal static int UAV_ReactiveMaskID = Shader.PropertyToID("UAV_ReactiveMask");
        internal static int KernelFogComposite = 0;
        internal static int KernelClearReactiveMask = 1;
        internal const string FogKeyword = "FOG_COMPOSITE_FOG";
        internal const string CloudKeyword = "FOG_COMPOSITE_CLOUD";
    }

    public partial class InfinityRenderPipeline
    {
        struct FogCompositePassData
        {
            public int2 resolution;
            public float maxDistance;
            public int hasFog;
            public int hasCloud;
            public Matrix4x4 matrix_InvViewProj;
            public Vector4 worldSpaceCameraPos;
            public ComputeShader fogCompositeShader;
            public RGTextureRef sceneColorTexture;
            public RGTextureRef foggedSceneColorTexture;
            public RGTextureRef depthTexture;
            public RGTextureRef volumetricFogTexture;
            public RGTextureRef volumetricCloudTexture;
        }

        struct ClearReactiveMaskPassData
        {
            public int2 resolution;
            public ComputeShader fogCompositeShader;
            public RGTextureRef reactiveMask;
        }

        void EnsureReactiveMask(Camera camera)
        {
            if (m_RGScoper.TryQueryTexture(InfinityShaderIDs.ReactiveMaskBuffer, out _))
            {
                return;
            }

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.fogCompositeShader, "ClearReactiveMask"))
            {
                throw new System.InvalidOperationException("InfinityRP: ReactiveMask is required but fogCompositeShader kernel ClearReactiveMask is missing.");
            }

            TextureDescriptor maskDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            maskDsc.name = FogCompositePassUtilityData.ReactiveMaskName;
            maskDsc.dimension = TextureDimension.Tex2D;
            maskDsc.colorFormat = GraphicsFormat.R8_UNorm;
            maskDsc.depthBufferBits = EDepthBits.None;
            maskDsc.enableRandomWrite = true;
            maskDsc.filterMode = FilterMode.Point;
            maskDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureRef reactiveMask = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.ReactiveMaskBuffer, maskDsc);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<ClearReactiveMaskPassData>(ProfilingSampler.Get(CustomSamplerId.ClearReactiveMask)))
            {
                ref ClearReactiveMaskPassData passData = ref passRef.GetPassData<ClearReactiveMaskPassData>();
                passData.resolution = new int2(camera.pixelWidth, camera.pixelHeight);
                passData.fogCompositeShader = pipelineAsset.fogCompositeShader;
                passData.reactiveMask = passRef.WriteTexture(reactiveMask);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in ClearReactiveMaskPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetComputeTextureParam(passData.fogCompositeShader, FogCompositePassUtilityData.KernelClearReactiveMask, FogCompositePassUtilityData.UAV_ReactiveMaskID, passData.reactiveMask);
                    cmdEncoder.DispatchCompute(
                        passData.fogCompositeShader,
                        FogCompositePassUtilityData.KernelClearReactiveMask,
                        Mathf.Max(1, Mathf.CeilToInt(passData.resolution.x / 8.0f)),
                        Mathf.Max(1, Mathf.CeilToInt(passData.resolution.y / 8.0f)),
                        1);
                });
            }
        }

        void ComputeFogComposite(RenderContext renderContext, Camera camera)
        {
            bool hasFog = m_RGScoper.TryQueryTexture(InfinityShaderIDs.VolumetricFogBuffer, out RGTextureRef volumetricFogTexture);
            bool hasCloud = m_RGScoper.TryQueryTexture(InfinityShaderIDs.VolumetricCloudBuffer, out RGTextureRef volumetricCloudTexture);
            if (!TranslucentFeatureUtility.ShouldRecordFogComposite(hasFog, hasCloud))
            {
                return;
            }

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.fogCompositeShader, "FogComposite"))
            {
                throw new System.InvalidOperationException("InfinityRP: Volumetric fog/cloud produced this frame but fogCompositeShader kernel FogComposite is missing.");
            }

            RGTextureRef sceneColorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.OpaqueSceneColorBuffer);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            float maxDistance = 64.0f;
            var volFog = ActiveVolumeStack.GetComponent<InfinityTech.Rendering.PostProcess.VolumetricFog>();
            maxDistance = volFog.MaxDistance.value;

            TextureDescriptor foggedDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            foggedDsc.name = "FoggedSceneColor";
            foggedDsc.dimension = TextureDimension.Tex2D;
            foggedDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            foggedDsc.depthBufferBits = EDepthBits.None;
            foggedDsc.enableRandomWrite = true;
            foggedDsc.filterMode = FilterMode.Bilinear;
            foggedDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureRef foggedSceneColor = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.FoggedSceneColorBuffer, foggedDsc);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<FogCompositePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeFogComposite)))
            {
                ref FogCompositePassData passData = ref passRef.GetPassData<FogCompositePassData>();
                passData.resolution = new int2(camera.pixelWidth, camera.pixelHeight);
                passData.maxDistance = maxDistance;
                passData.hasFog = hasFog ? 1 : 0;
                passData.hasCloud = hasCloud ? 1 : 0;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.fogCompositeShader = pipelineAsset.fogCompositeShader;
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
                passRef.SetExecuteFunc((in FogCompositePassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.fogCompositeShader;
                    cmdEncoder.SetComputeVectorParam(shader, FogCompositePassUtilityData.FogComposite_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeFloatParam(shader, FogCompositePassUtilityData.FogComposite_MaxDistanceID, passData.maxDistance);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(shader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);
                    shader.SetKeyword(new LocalKeyword(shader, FogCompositePassUtilityData.FogKeyword), passData.hasFog != 0);
                    shader.SetKeyword(new LocalKeyword(shader, FogCompositePassUtilityData.CloudKeyword), passData.hasCloud != 0);
                    cmdEncoder.SetComputeTextureParam(shader, FogCompositePassUtilityData.KernelFogComposite, FogCompositePassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, FogCompositePassUtilityData.KernelFogComposite, FogCompositePassUtilityData.SRV_SceneColorTextureID, passData.sceneColorTexture);
                    if (passData.hasFog != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, FogCompositePassUtilityData.KernelFogComposite, FogCompositePassUtilityData.SRV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    }
                    if (passData.hasCloud != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, FogCompositePassUtilityData.KernelFogComposite, FogCompositePassUtilityData.SRV_VolumetricCloudTextureID, passData.volumetricCloudTexture);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, FogCompositePassUtilityData.KernelFogComposite, FogCompositePassUtilityData.UAV_FoggedSceneColorTextureID, passData.foggedSceneColorTexture);
                    cmdEncoder.DispatchCompute(shader, FogCompositePassUtilityData.KernelFogComposite, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
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
