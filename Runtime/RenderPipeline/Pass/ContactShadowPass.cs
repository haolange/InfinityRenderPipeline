using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ContactShadowPassUtilityData
    {
        internal static string TextureName = "ContactShadowTexture";
        internal static int ContactShadow_NumStepsID = Shader.PropertyToID("ContactShadow_NumSteps");
        internal static int ContactShadow_MaxDistanceID = Shader.PropertyToID("ContactShadow_MaxDistance");
        internal static int ContactShadow_ThicknessID = Shader.PropertyToID("ContactShadow_Thickness");
        internal static int ContactShadow_IntensityID = Shader.PropertyToID("ContactShadow_Intensity");
        internal static int ContactShadow_FadeDistanceID = Shader.PropertyToID("ContactShadow_FadeDistance");
        internal static int ContactShadow_ResolutionID = Shader.PropertyToID("ContactShadow_Resolution");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int UAV_ContactShadowTextureID = Shader.PropertyToID("UAV_ContactShadowTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct ContactShadowPassData
        {
            public int numSteps;
            public float maxDistance;
            public float thickness;
            public float intensity;
            public float fadeDistance;
            public int2 resolution;
            public Matrix4x4 matrix_ViewProj;
            public Matrix4x4 matrix_InvViewProj;
            public ComputeShader contactShadowShader;
            public RGTextureRef depthTexture;
            public RGTextureRef contactShadowTexture;
        }

        void ComputeContactShadow(RenderContext renderContext, Camera camera)
        {
            var stack = VolumeManager.instance.stack;
            var contactShadowSettings = stack.GetComponent<ContactShadow>();
            if (contactShadowSettings == null) return;

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            TextureDescriptor contactShadowDsc = new TextureDescriptor(width, height);
            {
                contactShadowDsc.name = ContactShadowPassUtilityData.TextureName;
                contactShadowDsc.dimension = TextureDimension.Tex2D;
                contactShadowDsc.colorFormat = GraphicsFormat.R8_UNorm;
                contactShadowDsc.depthBufferBits = EDepthBits.None;
                contactShadowDsc.enableRandomWrite = true;
            }
            RGTextureRef contactShadowTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.ContactShadowBuffer, contactShadowDsc);

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            //Add ContactShadowPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<ContactShadowPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeContactShadow)))
            {
                //Setup Phase
                ref ContactShadowPassData passData = ref passRef.GetPassData<ContactShadowPassData>();
                passData.numSteps = contactShadowSettings.NumSteps.value;
                passData.maxDistance = contactShadowSettings.MaxDistance.value;
                passData.thickness = contactShadowSettings.Thickness.value;
                passData.intensity = contactShadowSettings.Intensity.value;
                passData.fadeDistance = contactShadowSettings.FadeDistance.value;
                passData.resolution = new int2(width, height);
                passData.matrix_ViewProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
                passData.matrix_InvViewProj = passData.matrix_ViewProj.inverse;
                passData.contactShadowShader = pipelineAsset.contactShadowShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.contactShadowTexture = passRef.WriteTexture(contactShadowTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in ContactShadowPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.contactShadowShader == null) return;

                    cmdEncoder.SetComputeIntParam(passData.contactShadowShader, ContactShadowPassUtilityData.ContactShadow_NumStepsID, passData.numSteps);
                    cmdEncoder.SetComputeFloatParam(passData.contactShadowShader, ContactShadowPassUtilityData.ContactShadow_MaxDistanceID, passData.maxDistance);
                    cmdEncoder.SetComputeFloatParam(passData.contactShadowShader, ContactShadowPassUtilityData.ContactShadow_ThicknessID, passData.thickness);
                    cmdEncoder.SetComputeFloatParam(passData.contactShadowShader, ContactShadowPassUtilityData.ContactShadow_IntensityID, passData.intensity);
                    cmdEncoder.SetComputeFloatParam(passData.contactShadowShader, ContactShadowPassUtilityData.ContactShadow_FadeDistanceID, passData.fadeDistance);
                    cmdEncoder.SetComputeVectorParam(passData.contactShadowShader, ContactShadowPassUtilityData.ContactShadow_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeTextureParam(passData.contactShadowShader, 0, ContactShadowPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.contactShadowShader, 0, ContactShadowPassUtilityData.UAV_ContactShadowTextureID, passData.contactShadowTexture);
                    cmdEncoder.DispatchCompute(passData.contactShadowShader, 0, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }
        }
    }
}
