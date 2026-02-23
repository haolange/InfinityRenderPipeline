using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class VolumetricCloudPassUtilityData
    {
        internal static string TextureName = "VolumetricCloudTexture";
        internal static int VolCloud_ResolutionID = Shader.PropertyToID("VolCloud_Resolution");
        internal static int VolCloud_CloudLayerBottomID = Shader.PropertyToID("VolCloud_CloudLayerBottom");
        internal static int VolCloud_CloudLayerThicknessID = Shader.PropertyToID("VolCloud_CloudLayerThickness");
        internal static int VolCloud_DensityMultiplierID = Shader.PropertyToID("VolCloud_DensityMultiplier");
        internal static int VolCloud_ShapeFactorID = Shader.PropertyToID("VolCloud_ShapeFactor");
        internal static int VolCloud_ErosionFactorID = Shader.PropertyToID("VolCloud_ErosionFactor");
        internal static int VolCloud_AnisotropyID = Shader.PropertyToID("VolCloud_Anisotropy");
        internal static int VolCloud_SilverIntensityID = Shader.PropertyToID("VolCloud_SilverIntensity");
        internal static int VolCloud_SilverSpreadID = Shader.PropertyToID("VolCloud_SilverSpread");
        internal static int VolCloud_AmbientIntensityID = Shader.PropertyToID("VolCloud_AmbientIntensity");
        internal static int VolCloud_NumPrimaryStepsID = Shader.PropertyToID("VolCloud_NumPrimarySteps");
        internal static int VolCloud_NumLightStepsID = Shader.PropertyToID("VolCloud_NumLightSteps");
        internal static int VolCloud_TemporalWeightID = Shader.PropertyToID("VolCloud_TemporalWeight");
        internal static int VolCloud_FrameIndexID = Shader.PropertyToID("VolCloud_FrameIndex");
        internal static int VolCloud_SunDirectionID = Shader.PropertyToID("VolCloud_SunDirection");
        internal static int VolCloud_SunColorID = Shader.PropertyToID("VolCloud_SunColor");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_TransmittanceLUTID = Shader.PropertyToID("SRV_TransmittanceLUT");
        internal static int UAV_VolumetricCloudTextureID = Shader.PropertyToID("UAV_VolumetricCloudTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct VolumetricCloudPassData
        {
            public float cloudLayerBottom;
            public float cloudLayerThickness;
            public float densityMultiplier;
            public float shapeFactor;
            public float erosionFactor;
            public float anisotropy;
            public float silverIntensity;
            public float silverSpread;
            public float ambientIntensity;
            public int numPrimarySteps;
            public int numLightSteps;
            public float temporalWeight;
            public int frameIndex;
            public int2 resolution;
            public Vector4 sunDirection;
            public Vector4 sunColor;
            public Matrix4x4 matrix_InvViewProj;
            public Vector4 worldSpaceCameraPos;
            public ComputeShader volumetricCloudShader;
            public RGTextureRef depthTexture;
            public RGTextureRef transmittanceLUT;
            public RGTextureRef volumetricCloudTexture;
        }

        void ComputeVolumetricCloud(RenderContext renderContext, Camera camera)
        {
            var stack = VolumeManager.instance.stack;
            var volCloud = stack.GetComponent<VolumetricCloud>();
            if (volCloud == null) return;

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            // Render at half-res for performance
            int cloudWidth = Mathf.Max(1, width >> 1);
            int cloudHeight = Mathf.Max(1, height >> 1);

            TextureDescriptor volumetricCloudDsc = new TextureDescriptor(cloudWidth, cloudHeight);
            {
                volumetricCloudDsc.name = VolumetricCloudPassUtilityData.TextureName;
                volumetricCloudDsc.dimension = TextureDimension.Tex2D;
                volumetricCloudDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                volumetricCloudDsc.depthBufferBits = EDepthBits.None;
                volumetricCloudDsc.enableRandomWrite = true;
            }
            RGTextureRef volumetricCloudTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.VolumetricCloudBuffer, volumetricCloudDsc);

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef transmittanceLUT = m_RGScoper.QueryTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT);

            Vector4 sunDirection = new Vector4(0, 1, 0, 0);
            Vector4 sunColor = new Vector4(1, 1, 1, 1);
            Light sunLight = RenderSettings.sun;
            if (sunLight != null)
            {
                sunDirection = -sunLight.transform.forward;
                sunColor = (Vector4)(sunLight.color * sunLight.intensity);
            }

            //Add VolumetricCloudPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<VolumetricCloudPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeVolumetricCloud)))
            {
                //Setup Phase
                ref VolumetricCloudPassData passData = ref passRef.GetPassData<VolumetricCloudPassData>();
                passData.cloudLayerBottom = volCloud.CloudLayerBottom.value;
                passData.cloudLayerThickness = volCloud.CloudLayerThickness.value;
                passData.densityMultiplier = volCloud.DensityMultiplier.value;
                passData.shapeFactor = volCloud.ShapeFactor.value;
                passData.erosionFactor = volCloud.ErosionFactor.value;
                passData.anisotropy = volCloud.Anisotropy.value;
                passData.silverIntensity = volCloud.SilverIntensity.value;
                passData.silverSpread = volCloud.SilverSpread.value;
                passData.ambientIntensity = volCloud.AmbientIntensity.value;
                passData.numPrimarySteps = volCloud.NumPrimarySteps.value;
                passData.numLightSteps = volCloud.NumLightSteps.value;
                passData.temporalWeight = volCloud.TemporalWeight.value;
                passData.frameIndex = Time.frameCount;
                passData.resolution = new int2(cloudWidth, cloudHeight);
                passData.sunDirection = sunDirection;
                passData.sunColor = sunColor;
                Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                passData.matrix_InvViewProj = (gpuProj * camera.worldToCameraMatrix).inverse;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.volumetricCloudShader = pipelineAsset.volumetricCloudShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.transmittanceLUT = passRef.ReadTexture(transmittanceLUT);
                passData.volumetricCloudTexture = passRef.WriteTexture(volumetricCloudTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in VolumetricCloudPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.volumetricCloudShader == null) return;

                    cmdEncoder.SetComputeVectorParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeFloatParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_CloudLayerBottomID, passData.cloudLayerBottom);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_CloudLayerThicknessID, passData.cloudLayerThickness);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_DensityMultiplierID, passData.densityMultiplier);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_ShapeFactorID, passData.shapeFactor);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_ErosionFactorID, passData.erosionFactor);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_AnisotropyID, passData.anisotropy);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_SilverIntensityID, passData.silverIntensity);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_SilverSpreadID, passData.silverSpread);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_AmbientIntensityID, passData.ambientIntensity);
                    cmdEncoder.SetComputeIntParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_NumPrimaryStepsID, passData.numPrimarySteps);
                    cmdEncoder.SetComputeIntParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_NumLightStepsID, passData.numLightSteps);
                    cmdEncoder.SetComputeFloatParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_TemporalWeightID, passData.temporalWeight);
                    cmdEncoder.SetComputeIntParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeVectorParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_SunDirectionID, passData.sunDirection);
                    cmdEncoder.SetComputeVectorParam(passData.volumetricCloudShader, VolumetricCloudPassUtilityData.VolCloud_SunColorID, passData.sunColor);
                    cmdEncoder.SetComputeMatrixParam(passData.volumetricCloudShader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(passData.volumetricCloudShader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);

                    cmdEncoder.SetComputeTextureParam(passData.volumetricCloudShader, 0, VolumetricCloudPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.volumetricCloudShader, 0, VolumetricCloudPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                    cmdEncoder.SetComputeTextureParam(passData.volumetricCloudShader, 0, VolumetricCloudPassUtilityData.UAV_VolumetricCloudTextureID, passData.volumetricCloudTexture);
                    cmdEncoder.DispatchCompute(passData.volumetricCloudShader, 0, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }
        }
    }
}
