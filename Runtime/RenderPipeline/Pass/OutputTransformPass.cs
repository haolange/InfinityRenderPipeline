using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class OutputTransformPassUtilityData
    {
        internal static string DisplayTextureName = "DisplayColorTexture";
        internal static int OT_ResolutionID = Shader.PropertyToID("OT_Resolution");
        internal static int OT_PolicyID = Shader.PropertyToID("OT_Policy");
        internal static int OT_ApplyRec2020ID = Shader.PropertyToID("OT_ApplyRec2020");
        internal static int OT_NitsScaleID = Shader.PropertyToID("OT_NitsScale");
        internal static int SRV_GradedColorID = Shader.PropertyToID("SRV_GradedColor");
        internal static int UAV_DisplayColorID = Shader.PropertyToID("UAV_DisplayColor");
        internal static int KernelOutputTransform = 0;
        internal const float PaperWhiteNits = 100.0f;
    }

    public partial class InfinityRenderPipeline
    {
        struct OutputTransformPassData
        {
            public int2 resolution;
            public int policy;
            public int applyRec2020;
            public float nitsScale;
            public ComputeShader outputTransformShader;
            public RGTextureRef gradedColor;
            public RGTextureRef displayColor;
        }

        void ComputeOutputTransform(Camera camera, in OutputTransformDecision decision)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.Display);

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.outputTransformShader, "OutputTransform"))
            {
                throw new InvalidOperationException("InfinityRP: Display is required but outputTransformShader kernel OutputTransform is missing.");
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.PostProcessBuffer, out RGTextureRef gradedColor))
            {
                throw new InvalidOperationException("InfinityRP: OutputTransform has no PostProcessBuffer input.");
            }

            TextureDescriptor displayDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            displayDsc.name = OutputTransformPassUtilityData.DisplayTextureName;
            displayDsc.dimension = TextureDimension.Tex2D;
            displayDsc.colorFormat = decision.displayFormat;
            displayDsc.depthBufferBits = EDepthBits.None;
            displayDsc.enableRandomWrite = true;
            displayDsc.filterMode = FilterMode.Bilinear;
            displayDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureRef displayColor = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DisplayColorBuffer, displayDsc);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<OutputTransformPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeOutputTransform)))
            {
                ref OutputTransformPassData passData = ref passRef.GetPassData<OutputTransformPassData>();
                passData.resolution = new int2(camera.pixelWidth, camera.pixelHeight);
                passData.policy = (int)decision.policy;
                passData.applyRec2020 = decision.outputGamut == OutputTransformUtility.OutputGamutRec2020 ? 1 : 0;
                passData.nitsScale = OutputTransformPassUtilityData.PaperWhiteNits;
                passData.outputTransformShader = pipelineAsset.outputTransformShader;
                passData.gradedColor = passRef.ReadTexture(gradedColor);
                passData.displayColor = passRef.WriteTexture(displayColor);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in OutputTransformPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.outputTransformShader;
                    cmdEncoder.SetComputeVectorParam(shader, OutputTransformPassUtilityData.OT_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeIntParam(shader, OutputTransformPassUtilityData.OT_PolicyID, passData.policy);
                    cmdEncoder.SetComputeIntParam(shader, OutputTransformPassUtilityData.OT_ApplyRec2020ID, passData.applyRec2020);
                    cmdEncoder.SetComputeFloatParam(shader, OutputTransformPassUtilityData.OT_NitsScaleID, passData.nitsScale);
                    cmdEncoder.SetComputeTextureParam(shader, OutputTransformPassUtilityData.KernelOutputTransform, OutputTransformPassUtilityData.SRV_GradedColorID, passData.gradedColor);
                    cmdEncoder.SetComputeTextureParam(shader, OutputTransformPassUtilityData.KernelOutputTransform, OutputTransformPassUtilityData.UAV_DisplayColorID, passData.displayColor);
                    cmdEncoder.DispatchCompute(shader, OutputTransformPassUtilityData.KernelOutputTransform, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }

            MarkFeatureProduced(EFrameFeature.Display);
        }
    }
}
