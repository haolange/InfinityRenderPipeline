using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class VolumetricFogPassUtilityData
    {
        internal static string TextureName = "VolumetricFogTexture";
        internal static int VolFog_ResolutionID = Shader.PropertyToID("VolFog_Resolution");
        internal static int VolFog_ScreenSizeID = Shader.PropertyToID("VolFog_ScreenSize");
        internal static int VolFog_DensityID = Shader.PropertyToID("VolFog_Density");
        internal static int VolFog_HeightID = Shader.PropertyToID("VolFog_Height");
        internal static int VolFog_HeightFalloffID = Shader.PropertyToID("VolFog_HeightFalloff");
        internal static int VolFog_AlbedoID = Shader.PropertyToID("VolFog_Albedo");
        internal static int VolFog_AnisotropyID = Shader.PropertyToID("VolFog_Anisotropy");
        internal static int VolFog_AmbientIntensityID = Shader.PropertyToID("VolFog_AmbientIntensity");
        internal static int VolFog_DepthSlicesID = Shader.PropertyToID("VolFog_DepthSlices");
        internal static int VolFog_MaxDistanceID = Shader.PropertyToID("VolFog_MaxDistance");
        internal static int VolFog_TemporalWeightID = Shader.PropertyToID("VolFog_TemporalWeight");
        internal static int VolFog_FrameIndexID = Shader.PropertyToID("VolFog_FrameIndex");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_CascadeShadowMapID = Shader.PropertyToID("SRV_CascadeShadowMap");
        internal static int UAV_VolumetricFogTextureID = Shader.PropertyToID("UAV_VolumetricFogTexture");
        internal static int KernelScatterDensity = 0;
        internal static int KernelIntegrate = 1;
    }

    public partial class InfinityRenderPipeline
    {
        struct VolumetricFogPassData
        {
            public float density;
            public float height;
            public float heightFalloff;
            public Color albedo;
            public float anisotropy;
            public float ambientIntensity;
            public int depthSlices;
            public float maxDistance;
            public float temporalWeight;
            public int frameIndex;
            public int2 screenSize;
            public int3 froxelResolution;
            public Matrix4x4 matrix_InvViewProj;
            public Vector4 worldSpaceCameraPos;
            public ComputeShader volumetricFogShader;
            public RGTextureRef depthTexture;
            public RGTextureRef cascadeShadowMap;
            public RGTextureRef volumetricFogTexture;
        }

        void ComputeVolumetricFog(RenderContext renderContext, Camera camera)
        {
            var stack = VolumeManager.instance.stack;
            var volFog = stack.GetComponent<VolumetricFog>();
            if (volFog == null) return;

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            int depthSlices = volFog.DepthSlices.value;
            int froxelWidth = Mathf.CeilToInt(width / 8.0f);
            int froxelHeight = Mathf.CeilToInt(height / 8.0f);

            TextureDescriptor volumetricFogDsc = new TextureDescriptor(froxelWidth, froxelHeight);
            {
                volumetricFogDsc.name = VolumetricFogPassUtilityData.TextureName;
                volumetricFogDsc.dimension = TextureDimension.Tex3D;
                volumetricFogDsc.slices = depthSlices;
                volumetricFogDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                volumetricFogDsc.depthBufferBits = EDepthBits.None;
                volumetricFogDsc.enableRandomWrite = true;
            }
            RGTextureRef volumetricFogTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.VolumetricFogBuffer, volumetricFogDsc);

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef cascadeShadowMap = m_RGScoper.QueryTexture(InfinityShaderIDs.CascadeShadowMap);

            //Add VolumetricFogPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<VolumetricFogPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeVolumetricFog)))
            {
                //Setup Phase
                ref VolumetricFogPassData passData = ref passRef.GetPassData<VolumetricFogPassData>();
                passData.density = volFog.Density.value;
                passData.height = volFog.Height.value;
                passData.heightFalloff = volFog.HeightFalloff.value;
                passData.albedo = volFog.Albedo.value;
                passData.anisotropy = volFog.Anisotropy.value;
                passData.ambientIntensity = volFog.AmbientIntensity.value;
                passData.depthSlices = depthSlices;
                passData.maxDistance = volFog.MaxDistance.value;
                passData.temporalWeight = volFog.TemporalWeight.value;
                passData.frameIndex = Time.frameCount;
                passData.screenSize = new int2(width, height);
                passData.froxelResolution = new int3(froxelWidth, froxelHeight, depthSlices);
                Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                passData.matrix_InvViewProj = (gpuProj * camera.worldToCameraMatrix).inverse;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.volumetricFogShader = pipelineAsset.volumetricFogShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.cascadeShadowMap = passRef.ReadTexture(cascadeShadowMap);
                passData.volumetricFogTexture = passRef.WriteTexture(volumetricFogTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in VolumetricFogPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.volumetricFogShader == null) return;

                    cmdEncoder.SetComputeVectorParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_ScreenSizeID, new Vector4(passData.screenSize.x, passData.screenSize.y, 1.0f / passData.screenSize.x, 1.0f / passData.screenSize.y));
                    cmdEncoder.SetComputeVectorParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_ResolutionID, new Vector4(passData.froxelResolution.x, passData.froxelResolution.y, passData.froxelResolution.z, 0));
                    cmdEncoder.SetComputeFloatParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_DensityID, passData.density);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_HeightID, passData.height);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_HeightFalloffID, passData.heightFalloff);
                    cmdEncoder.SetComputeVectorParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_AlbedoID, (Vector4)passData.albedo);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_AnisotropyID, passData.anisotropy);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_AmbientIntensityID, passData.ambientIntensity);
                    cmdEncoder.SetComputeIntParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_DepthSlicesID, passData.depthSlices);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_MaxDistanceID, passData.maxDistance);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_TemporalWeightID, passData.temporalWeight);
                    cmdEncoder.SetComputeIntParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.VolFog_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeMatrixParam(passData.volumetricFogShader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(passData.volumetricFogShader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);

                    // Kernel 0: Scatter + Density
                    cmdEncoder.SetComputeTextureParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.KernelScatterDensity, VolumetricFogPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.KernelScatterDensity, VolumetricFogPassUtilityData.SRV_CascadeShadowMapID, passData.cascadeShadowMap);
                    cmdEncoder.SetComputeTextureParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.KernelScatterDensity, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.DispatchCompute(passData.volumetricFogShader, VolumetricFogPassUtilityData.KernelScatterDensity, Mathf.CeilToInt(passData.froxelResolution.x / 8.0f), Mathf.CeilToInt(passData.froxelResolution.y / 8.0f), 1);

                    // Kernel 1: Integrate along z-axis
                    cmdEncoder.SetComputeTextureParam(passData.volumetricFogShader, VolumetricFogPassUtilityData.KernelIntegrate, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.DispatchCompute(passData.volumetricFogShader, VolumetricFogPassUtilityData.KernelIntegrate, Mathf.CeilToInt(passData.froxelResolution.x / 8.0f), Mathf.CeilToInt(passData.froxelResolution.y / 8.0f), 1);
                });
            }
        }
    }
}
