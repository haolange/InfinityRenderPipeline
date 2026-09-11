using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class RTAOPassUtilityData
    {
        internal static string OcclusionTextureName = "RTAOOcclusionTexture";
        internal static int KernelTrace = 0;
        internal static int NumRaysID = Shader.PropertyToID("RTAO_NumRays");
        internal static int RadiusID = Shader.PropertyToID("RTAO_Radius");
        internal static int ResolutionID = Shader.PropertyToID("RTAO_Resolution");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_NormalTextureID = Shader.PropertyToID("SRV_NormalTexture");
        internal static int SRV_HiZTextureID = Shader.PropertyToID("SRV_HiZTexture");
        internal static int UAV_OcclusionTextureID = Shader.PropertyToID("UAV_OcclusionTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct RTAOPassData
        {
            public int2 resolution;
            public int numRays;
            public float radius;
            public ComputeShader shader;
            public RGTextureRef depthTexture;
            public RGTextureRef normalTexture;
            public RGTextureRef hiZTexture;
            public RGTextureRef occlusionTexture;
        }

        void ComputeRayTracedOcclusion(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            if (!ShouldRecordFeature(EFrameFeature.RTAO))
            {
                return;
            }

            RayTracingAmbientOcclusion settings = ActiveVolumeStack.GetComponent<RayTracingAmbientOcclusion>();
            if (!VolumeComponentActive(settings))
            {
                throw new System.InvalidOperationException("InfinityRP: RTAO is marked to produce but the Volume is inactive.");
            }

            if (!GraphicsUtility.HasRequiredKernels(shaders.rtaoShader, "RTAOTrace"))
            {
                throw new System.InvalidOperationException("InfinityRP: RTAO is active but rtaoShader kernel RTAOTrace is missing.");
            }

            InfinityDebugDisplaySettings.current.lighting.rayTracingBackend = InfinityRayTracingEnvironment.lastBackend;

            TextureDescriptor occlusionDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            occlusionDsc.name = RTAOPassUtilityData.OcclusionTextureName;
            occlusionDsc.dimension = TextureDimension.Tex2D;
            occlusionDsc.colorFormat = GraphicsFormat.R16_SFloat;
            occlusionDsc.enableRandomWrite = true;
            occlusionDsc.filterMode = FilterMode.Bilinear;
            occlusionDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureRef occlusion = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.OcclusionBuffer, occlusionDsc);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<RTAOPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeRayTracedOcclusion)))
            {
                ref RTAOPassData passData = ref passRef.GetPassData<RTAOPassData>();
                passData.resolution = new int2(camera.pixelWidth, camera.pixelHeight);
                passData.numRays = settings.NumRays.value;
                passData.radius = settings.Radius.value;
                passData.shader = shaders.rtaoShader;
                passData.depthTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.normalTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB));
                if (m_RGScoper.TryQueryTexture(InfinityShaderIDs.HiZBuffer, out RGTextureRef hiZ))
                {
                    passData.hiZTexture = passRef.ReadTexture(hiZ);
                }
                passData.occlusionTexture = passRef.WriteTexture(occlusion);
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in RTAOPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.shader;
                    cmdEncoder.SetComputeIntParam(shader, RTAOPassUtilityData.NumRaysID, passData.numRays);
                    cmdEncoder.SetComputeFloatParam(shader, RTAOPassUtilityData.RadiusID, passData.radius);
                    cmdEncoder.SetComputeVectorParam(shader, RTAOPassUtilityData.ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeTextureParam(shader, RTAOPassUtilityData.KernelTrace, RTAOPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, RTAOPassUtilityData.KernelTrace, RTAOPassUtilityData.SRV_NormalTextureID, passData.normalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, RTAOPassUtilityData.KernelTrace, RTAOPassUtilityData.SRV_HiZTextureID, passData.hiZTexture);
                    cmdEncoder.SetComputeTextureParam(shader, RTAOPassUtilityData.KernelTrace, RTAOPassUtilityData.UAV_OcclusionTextureID, passData.occlusionTexture);
                    cmdEncoder.DispatchCompute(shader, RTAOPassUtilityData.KernelTrace, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }

            MarkFeatureProduced(EFrameFeature.RTAO);
        }
    }
}
