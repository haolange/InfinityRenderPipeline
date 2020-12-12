using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace InfinityTech.Runtime.Rendering.Feature
{
    public static class SVGF_SpatialShaderID
    {
        public static int FrameIndex = Shader.PropertyToID("SVGF_FrameIndex");
        public static int SpatialRadius = Shader.PropertyToID("SVGF_SpatialRadius");
        public static int SpatialSize = Shader.PropertyToID("SVGF_SpatialSize");
        public static int Matrix_InvProj = Shader.PropertyToID("Matrix_InvProj");
        public static int Matrix_InvViewProj = Shader.PropertyToID("Matrix_InvViewProj");
        public static int Matrix_WorldToView = Shader.PropertyToID("Matrix_WorldToView");
        public static int SRV_SceneDepth = Shader.PropertyToID("SRV_SceneDepth");
        public static int SRV_GBufferNormal = Shader.PropertyToID("SRV_GBufferNormal");
        public static int SRV_GBufferRoughness = Shader.PropertyToID("SRV_GBufferRoughness");
        public static int SRV_UWVPDF = Shader.PropertyToID("SRV_UWVPDF");
        public static int SRV_ColorMask = Shader.PropertyToID("SRV_ColorMask");
        public static int UAV_SpatialColor = Shader.PropertyToID("UAV_SpatialColor");
    }

    public static class SVGF_TemporalShaderID
    {
        public static int TemporalScale = Shader.PropertyToID("SVGF_TemporalScale");
        public static int TemporalWeight = Shader.PropertyToID("SVGF_TemporalWeight");
        public static int TemporalSize = Shader.PropertyToID("SVGF_TemporalSize");
        public static int Matrix_PrevViewProj = Shader.PropertyToID("Matrix_PrevViewProj");
        public static int Matrix_ViewProj = Shader.PropertyToID("Matrix_ViewProj");
        public static int Matrix_InvViewProj = Shader.PropertyToID("Matrix_InvViewProj");
        public static int SRV_CurrColor = Shader.PropertyToID("SRV_CurrColor");
        public static int SRV_PrevColor = Shader.PropertyToID("SRV_PrevColor");
        public static int SRV_GBufferMotion = Shader.PropertyToID("SRV_GBufferMotion");
        public static int SRV_RayDepth = Shader.PropertyToID("SRV_RayDepth");
        public static int SRV_GBufferNormal = Shader.PropertyToID("SRV_GBufferNormal");
        public static int UAV_TemporalColor = Shader.PropertyToID("UAV_TemporalColor");
    }

    public static class SVGF_BilateralShaderID
    {
        public static int BilateralRadius = Shader.PropertyToID("SVGF_BilateralRadius");
        public static int ColorWeight = Shader.PropertyToID("SVGF_ColorWeight");
        public static int NormalWeight = Shader.PropertyToID("SVGF_NormalWeight");
        public static int DepthWeight = Shader.PropertyToID("SVGF_DepthWeight");
        public static int BilateralSize = Shader.PropertyToID("SVGF_BilateralSize");
        public static int SRV_InputColor = Shader.PropertyToID("SRV_InputColor");
        public static int SRV_GBufferNormal = Shader.PropertyToID("SRV_GBufferNormal");
        public static int SRV_SceneDepth = Shader.PropertyToID("SRV_SceneDepth");
        public static int UAV_BilateralColor = Shader.PropertyToID("UAV_BilateralColor");
    }

    public struct SVGFParameterDescriptor
    {
        public int NumSpatial;
        public float SpatialRadius;
        public float TemporalScale;
        public float TemporalWeight;
        public float BilateralRadius;
        public float BilateralColorWeight;
        public float BilateralDepthWeight;
        public float BilateralNormalWeight;
    }

    public struct SVGFInputDescriptor {
        public int FrameIndex;
        public float4 Resolution;
        public float4x4 Matrix_PrevViewProj;
        public float4x4 Matrix_InvProj;
        public float4x4 Matrix_ViewProj;
        public float4x4 Matrix_InvViewProj;
        public float4x4 Matrix_WorldToView;
        public RenderTargetIdentifier SRV_SceneDepth;
        public RenderTargetIdentifier SRV_GBufferMotion;
        public RenderTargetIdentifier SRV_GBufferNormal;
        public RenderTargetIdentifier SRV_GBufferRoughness;
    }


    public static class SVGFilter
    {
        private static ComputeShader SVGF_Shader {
            get {
                return Resources.Load<ComputeShader>("Shaders/SVGF_Shader");
            }
        }

        public static void SpatialFilter(CommandBuffer CmdBuffer, RenderTargetIdentifier SRV_UWVPDF, RenderTargetIdentifier SRV_ColorMask, RenderTargetIdentifier UAV_SpatialColor, ref SVGFParameterDescriptor Parameters, ref SVGFInputDescriptor InputData)
        {
            CmdBuffer.SetComputeIntParam(SVGF_Shader, SVGF_SpatialShaderID.FrameIndex, InputData.FrameIndex);
            CmdBuffer.SetComputeFloatParam(SVGF_Shader, SVGF_SpatialShaderID.SpatialRadius, Parameters.SpatialRadius);
            CmdBuffer.SetComputeVectorParam(SVGF_Shader, SVGF_SpatialShaderID.SpatialSize, InputData.Resolution);
            CmdBuffer.SetComputeMatrixParam(SVGF_Shader, SVGF_SpatialShaderID.Matrix_InvProj, InputData.Matrix_InvProj);
            CmdBuffer.SetComputeMatrixParam(SVGF_Shader, SVGF_SpatialShaderID.Matrix_InvViewProj, InputData.Matrix_InvViewProj);
            CmdBuffer.SetComputeMatrixParam(SVGF_Shader, SVGF_SpatialShaderID.Matrix_WorldToView, InputData.Matrix_WorldToView);

            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 0, SVGF_SpatialShaderID.SRV_SceneDepth, InputData.SRV_SceneDepth);
            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 0, SVGF_SpatialShaderID.SRV_GBufferNormal, InputData.SRV_GBufferNormal);
            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 0, SVGF_SpatialShaderID.SRV_GBufferRoughness, InputData.SRV_GBufferRoughness);
            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 0, SVGF_SpatialShaderID.SRV_UWVPDF, SRV_UWVPDF);
            for (uint i = 0; i < (uint)Parameters.NumSpatial; i++) {
                uint CurrState = i & 1;
                if(CurrState == 0) {
                    CmdBuffer.SetComputeTextureParam(SVGF_Shader, 0, SVGF_SpatialShaderID.SRV_ColorMask, SRV_ColorMask);
                    CmdBuffer.SetComputeTextureParam(SVGF_Shader, 0, SVGF_SpatialShaderID.UAV_SpatialColor, UAV_SpatialColor);
                    CmdBuffer.DispatchCompute(SVGF_Shader, 0, Mathf.CeilToInt(InputData.Resolution.x / 16), Mathf.CeilToInt(InputData.Resolution.y / 16), 1);
                } else {
                    CmdBuffer.SetComputeTextureParam(SVGF_Shader, 0, SVGF_SpatialShaderID.SRV_ColorMask, UAV_SpatialColor);
                    CmdBuffer.SetComputeTextureParam(SVGF_Shader, 0, SVGF_SpatialShaderID.UAV_SpatialColor, SRV_ColorMask);
                    CmdBuffer.DispatchCompute(SVGF_Shader, 0, Mathf.CeilToInt(InputData.Resolution.x / 16), Mathf.CeilToInt(InputData.Resolution.y / 16), 1);
                    CmdBuffer.CopyTexture(SRV_ColorMask, UAV_SpatialColor);
                }
            }
        }

        public static void TemporalFilter(CommandBuffer CmdBuffer, RenderTargetIdentifier SRV_RayDepth, RenderTargetIdentifier SRV_CurrColor, RenderTargetIdentifier SRV_PrevColor, RenderTargetIdentifier UAV_TemporalColor, ref SVGFParameterDescriptor Parameters, ref SVGFInputDescriptor InputData)
        {
            CmdBuffer.SetComputeFloatParam(SVGF_Shader, SVGF_TemporalShaderID.TemporalScale, Parameters.TemporalScale);
            CmdBuffer.SetComputeFloatParam(SVGF_Shader, SVGF_TemporalShaderID.TemporalWeight, Parameters.TemporalWeight);
            CmdBuffer.SetComputeVectorParam(SVGF_Shader, SVGF_TemporalShaderID.TemporalSize, InputData.Resolution);
            CmdBuffer.SetComputeMatrixParam(SVGF_Shader, SVGF_TemporalShaderID.Matrix_PrevViewProj, InputData.Matrix_PrevViewProj);
            CmdBuffer.SetComputeMatrixParam(SVGF_Shader, SVGF_TemporalShaderID.Matrix_ViewProj, InputData.Matrix_ViewProj);
            CmdBuffer.SetComputeMatrixParam(SVGF_Shader, SVGF_TemporalShaderID.Matrix_InvViewProj, InputData.Matrix_InvViewProj);
            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 1, SVGF_TemporalShaderID.SRV_GBufferMotion, InputData.SRV_GBufferMotion);
            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 1, SVGF_TemporalShaderID.SRV_RayDepth, SRV_RayDepth);
            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 1, SVGF_TemporalShaderID.SRV_CurrColor, SRV_CurrColor);
            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 1, SVGF_TemporalShaderID.SRV_PrevColor, SRV_PrevColor);
            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 1, SVGF_TemporalShaderID.UAV_TemporalColor, UAV_TemporalColor);
            CmdBuffer.DispatchCompute(SVGF_Shader, 1,  Mathf.CeilToInt(InputData.Resolution.x / 16),  Mathf.CeilToInt(InputData.Resolution.y / 16), 1);
        }

        public static void BilateralFilter(CommandBuffer CmdBuffer, RenderTargetIdentifier SRV_InputColor, RenderTargetIdentifier UAV_BilateralColor, ref SVGFParameterDescriptor Parameters, ref SVGFInputDescriptor InputData)
        {
            CmdBuffer.SetComputeFloatParam(SVGF_Shader, SVGF_BilateralShaderID.BilateralRadius, Parameters.BilateralRadius);
            CmdBuffer.SetComputeFloatParam(SVGF_Shader, SVGF_BilateralShaderID.ColorWeight, Parameters.BilateralColorWeight);
            CmdBuffer.SetComputeFloatParam(SVGF_Shader, SVGF_BilateralShaderID.NormalWeight, Parameters.BilateralNormalWeight);
            CmdBuffer.SetComputeFloatParam(SVGF_Shader, SVGF_BilateralShaderID.DepthWeight, Parameters.BilateralDepthWeight);
            CmdBuffer.SetComputeVectorParam(SVGF_Shader, SVGF_BilateralShaderID.BilateralSize, InputData.Resolution);

            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 1, SVGF_BilateralShaderID.SRV_InputColor, SRV_InputColor);
            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 1, SVGF_BilateralShaderID.SRV_GBufferNormal, InputData.SRV_GBufferNormal);
            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 1, SVGF_BilateralShaderID.SRV_SceneDepth, InputData.SRV_SceneDepth);
            CmdBuffer.SetComputeTextureParam(SVGF_Shader, 1, SVGF_BilateralShaderID.UAV_BilateralColor, UAV_BilateralColor);
            CmdBuffer.DispatchCompute(SVGF_Shader, 1,  Mathf.CeilToInt(InputData.Resolution.x / 16),  Mathf.CeilToInt(InputData.Resolution.y / 16), 1);
            //CmdBuffer.CopyTexture(UAV_BilateralColor, InputData.SRV_PrevColor);
        }
    }
}
