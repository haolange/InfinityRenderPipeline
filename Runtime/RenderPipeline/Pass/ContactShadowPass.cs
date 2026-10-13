using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ContactShadowPassUtilityData
    {
        internal const string KernelName = "ContactShadowCS";
        internal static string SunTextureName = "ContactShadowSun";
        internal static string MaskTextureName = "ContactShadowMask";
        internal static int ContactShadow_NumStepsID = Shader.PropertyToID("ContactShadow_NumSteps");
        internal static int ContactShadow_SunNumStepsID = Shader.PropertyToID("ContactShadow_SunNumSteps");
        internal static int ContactShadow_FrameIndexID = Shader.PropertyToID("ContactShadow_FrameIndex");
        internal static int ContactShadow_MaxDistanceID = Shader.PropertyToID("ContactShadow_MaxDistance");
        internal static int ContactShadow_ThicknessID = Shader.PropertyToID("ContactShadow_Thickness");
        internal static int ContactShadow_IntensityID = Shader.PropertyToID("ContactShadow_Intensity");
        internal static int ContactShadow_FadeDistanceID = Shader.PropertyToID("ContactShadow_FadeDistance");
        internal static int ContactShadow_ResolutionID = Shader.PropertyToID("ContactShadow_Resolution");
        internal static int ContactShadow_DepthParamsID = Shader.PropertyToID("ContactShadow_DepthParams");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int UAV_ContactShadowSunID = Shader.PropertyToID("UAV_ContactShadowSun");
        internal static int UAV_ContactShadowMaskID = Shader.PropertyToID("UAV_ContactShadowMask");
        // Consumer side (Deferred keyword DEFERRED_CONTACT_SHADOW; Forward globals guarded by g_ContactShadowEnabled).
        internal static int SRV_ContactShadowSunID = Shader.PropertyToID("SRV_ContactShadowSun");
        internal static int SRV_ContactShadowMaskID = Shader.PropertyToID("SRV_ContactShadowMask");
        internal static int ContactShadowEnabledID = Shader.PropertyToID("g_ContactShadowEnabled");

        // x, y = m22, m23 of the GPU projection (linear view depth from device depth), z = 1 when orthographic.
        internal static Vector4 DepthParams(Camera camera)
        {
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            return new Vector4(projection.m22, projection.m23, camera.orthographic ? 1 : 0, 0);
        }
    }

    public partial class InfinityRenderPipeline
    {
        struct ContactShadowPassData
        {
            public FContactShadowSettings settings;
            public int frameIndex;
            public int2 resolution;
            public Vector4 depthParams;
            public Matrix4x4 matrix_ViewProj;
            public Matrix4x4 matrix_InvViewProj;
            public Vector4 worldSpaceCameraPos;
            public FLightLoopInputs lightLoop;
            public RGBufferRef lightRecordBuffer;
            public ComputeShader contactShadowShader;
            public RGTextureRef depthTexture;
            public RGTextureRef sunTexture;
            public RGTextureRef maskTexture;
        }

        // Requested in the content phase: the Volume is active and the sun or a local light has Contact Shadows on.
        void ComputeContactShadow(RenderContext renderContext, Camera camera)
        {
            if (!ShouldRecordFeature(EFrameFeature.ContactShadow))
            {
                return;
            }
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.ContactShadow);
            FContactShadowSettings settings = ActiveVolumeStack.GetComponent<ContactShadow>().Resolve();

            int width = m_ActiveFrameState.dimensions.internalSize.x;
            int height = m_ActiveFrameState.dimensions.internalSize.y;

            TextureDescriptor sunDsc = new TextureDescriptor(width, height)
            {
                name = ContactShadowPassUtilityData.SunTextureName,
                dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R8_UNorm,
                depthBufferBits = EDepthBits.None,
                enableRandomWrite = true
            };
            RGTextureRef sunTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.ContactShadowBuffer, sunDsc);
            TextureDescriptor maskDsc = sunDsc;
            maskDsc.name = ContactShadowPassUtilityData.MaskTextureName;
            maskDsc.colorFormat = GraphicsFormat.R32_UInt;
            RGTextureRef maskTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.ContactShadowMaskBuffer, maskDsc);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<ContactShadowPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeContactShadow)))
            {
                ref ContactShadowPassData passData = ref passRef.GetPassData<ContactShadowPassData>();
                passData.settings = settings;
                passData.frameIndex = Time.frameCount;
                passData.resolution = new int2(width, height);
                passData.depthParams = ContactShadowPassUtilityData.DepthParams(camera);
                passData.matrix_ViewProj = m_CameraUniform.matrix_ViewFlipYJitterProj;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.worldSpaceCameraPos = camera.transform.position;
                FLightLoopInputs lightLoop = QueryLightLoop(renderContext);
                lightLoop.zBins = passRef.ReadBuffer(lightLoop.zBins);
                lightLoop.tileMasks = passRef.ReadBuffer(lightLoop.tileMasks);
                passData.lightLoop = lightLoop;
                passData.lightRecordBuffer = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                passData.contactShadowShader = shaders.contactShadowShader;
                passData.depthTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.sunTexture = passRef.WriteTexture(sunTexture);
                passData.maskTexture = passRef.WriteTexture(maskTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in ContactShadowPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.contactShadowShader;
                    int kernel = shader.FindKernel(ContactShadowPassUtilityData.KernelName);
                    FContactShadowSettings s = passData.settings;
                    cmdEncoder.SetComputeIntParam(shader, ContactShadowPassUtilityData.ContactShadow_NumStepsID, s.numSteps);
                    cmdEncoder.SetComputeIntParam(shader, ContactShadowPassUtilityData.ContactShadow_SunNumStepsID, s.sunNumSteps);
                    cmdEncoder.SetComputeIntParam(shader, ContactShadowPassUtilityData.ContactShadow_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeFloatParam(shader, ContactShadowPassUtilityData.ContactShadow_MaxDistanceID, s.maxDistance);
                    cmdEncoder.SetComputeFloatParam(shader, ContactShadowPassUtilityData.ContactShadow_ThicknessID, s.thickness);
                    cmdEncoder.SetComputeFloatParam(shader, ContactShadowPassUtilityData.ContactShadow_IntensityID, s.intensity);
                    cmdEncoder.SetComputeFloatParam(shader, ContactShadowPassUtilityData.ContactShadow_FadeDistanceID, s.fadeDistance);
                    cmdEncoder.SetComputeVectorParam(shader, ContactShadowPassUtilityData.ContactShadow_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeVectorParam(shader, ContactShadowPassUtilityData.ContactShadow_DepthParamsID, passData.depthParams);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_ViewProj"), passData.matrix_ViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(shader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);
                    passData.lightLoop.Bind(cmdEncoder, shader, kernel);
                    cmdEncoder.SetComputeBufferParam(shader, kernel, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    cmdEncoder.SetComputeTextureParam(shader, kernel, ContactShadowPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, kernel, ContactShadowPassUtilityData.UAV_ContactShadowSunID, passData.sunTexture);
                    cmdEncoder.SetComputeTextureParam(shader, kernel, ContactShadowPassUtilityData.UAV_ContactShadowMaskID, passData.maskTexture);
                    cmdEncoder.DispatchCompute(shader, kernel, (passData.resolution.x + 7) / 8, (passData.resolution.y + 7) / 8, 1);
                });
            }

            MarkFeatureProduced(EFrameFeature.ContactShadow);
        }
    }
}
