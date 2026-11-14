using System.Runtime.InteropServices;
using UnityEngine;
using InfinityTech.Core;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Volume;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_ContactShadowParameters Shaders/Pass/Shadow/Compute_ContactShadow.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct ContactShadowParameters
    {
        public Vector4 resolution;
        public Vector4 depthParams;
        public int numSteps;
        public int sunNumSteps;
        public int frameIndex;
        public float maxDistance;
        public float thickness;
        public float intensity;
        public float fadeDistance;
        public int padding0;
    }

    internal static class ContactShadowPassUtilityData
    {
        internal static readonly int CBV_ContactShadowParametersID = Shader.PropertyToID("CBV_ContactShadowParameters");
        internal static readonly string SunTextureName = "ContactShadowSun";
        internal static readonly string MaskTextureName = "ContactShadowMask";
        internal static readonly int UAV_ContactShadowSunID = Shader.PropertyToID("UAV_ContactShadowSun");
        internal static readonly int UAV_ContactShadowMaskID = Shader.PropertyToID("UAV_ContactShadowMask");
        // Consumers compile contact resources only in their enabled Deferred or Forward variant.
        internal static readonly int SRV_ContactShadowSunID = Shader.PropertyToID("SRV_ContactShadowSun");
        internal static readonly int SRV_ContactShadowMaskID = Shader.PropertyToID("SRV_ContactShadowMask");

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
            public RGConstantsHandle parameters;
            public int2 resolution;
            public LightLoopInputs lightLoop;
            public RGBufferHandle lightRecordBuffer;
            public ComputeKernel kernel;
            public RGTextureHandle depthTexture;
            public RGTextureHandle sunTexture;
            public RGTextureHandle maskTexture;
        }

        // Requested in the content phase: the Volume is active and the sun or a local light has Contact Shadows on.
        void ComputeContactShadow(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.ContactShadow))
            {
                return;
            }
            context.features.ThrowIfCannotProduce(EFrameFeature.ContactShadow);
            ContactShadowSettings settings = context.volumes.contactShadow.Resolve();

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;

            TextureDescriptor sunDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal)
            {
                name = ContactShadowPassUtilityData.SunTextureName,
                dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R8_UNorm,
                depthBufferBits = EDepthBits.None,
                enableRandomWrite = true
            };
            RGTextureHandle sunTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.ContactShadowBuffer, sunDsc);
            TextureDescriptor maskDsc = sunDsc;
            maskDsc.name = ContactShadowPassUtilityData.MaskTextureName;
            maskDsc.colorFormat = GraphicsFormat.R32_UInt;
            RGTextureHandle maskTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.ContactShadowMaskBuffer, maskDsc);

            using (var passRef = m_RGBuilder.AddComputePass<ContactShadowPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeContactShadow)))
            {
                ref ContactShadowPassData passData = ref passRef.GetPassData();
                passData.resolution = new int2(width, height);
                passData.parameters = passRef.WriteConstants(new ContactShadowParameters
                {
                    resolution = new Vector4(width, height, 1.0f / width, 1.0f / height),
                    depthParams = ContactShadowPassUtilityData.DepthParams(context.camera),
                    numSteps = settings.numSteps,
                    sunNumSteps = settings.sunNumSteps,
                    frameIndex = Time.frameCount,
                    maxDistance = settings.maxDistance,
                    thickness = settings.thickness,
                    intensity = settings.intensity,
                    fadeDistance = settings.fadeDistance
                });
                LightLoopInputs lightLoop = QueryLightLoop(context);
                lightLoop.Read(passRef);
                passData.lightLoop = lightLoop;
                passData.lightRecordBuffer = passRef.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                passData.kernel = kernels.contactShadow;
                passData.depthTexture = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.sunTexture = passRef.WriteTexture(sunTexture);
                passData.maskTexture = passRef.WriteTexture(maskTexture);

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in ContactShadowPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeKernel kernel = passData.kernel;
                    ComputeShader shader = kernel.shader;
                    cmdEncoder.SetComputeConstantBufferParam(shader, ContactShadowPassUtilityData.CBV_ContactShadowParametersID, passData.parameters);
                    passData.lightLoop.Bind(cmdEncoder, shader, kernel.index);
                    cmdEncoder.SetComputeBufferParam(shader, kernel.index, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    cmdEncoder.SetComputeTextureParam(shader, kernel.index, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, kernel.index, ContactShadowPassUtilityData.UAV_ContactShadowSunID, passData.sunTexture);
                    cmdEncoder.SetComputeTextureParam(shader, kernel.index, ContactShadowPassUtilityData.UAV_ContactShadowMaskID, passData.maskTexture);
                    cmdEncoder.DispatchCompute(shader, kernel.index, kernel.GroupsX(passData.resolution.x), kernel.GroupsY(passData.resolution.y), 1);
                });
            }

            context.features.MarkProduced(EFrameFeature.ContactShadow);
        }
    }
}
