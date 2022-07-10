using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GraphicsFeature
{
    public struct SSRParameterDescriptor
    {
        public int numRays;
        public int numSteps;
        public int numSpatial;
        public float brdfBias;
        public float fadeness;
        public float roughness;
        public float spatialRadius;
        public float temporalScale;
        public float temporalWeight;
    }

    public struct SSRInputDescriptor
    {
        public int frameIndex;
        public float4 resolution;
        public float4 filterResolution;
        public float4x4 matrix_Proj;
        public float4x4 matrix_InvProj;
        public float4x4 matrix_ViewProj;
        public float4x4 matrix_InvViewProj;
        public float4x4 matrix_LastViewProj;
        public float4x4 matrix_WorldToView;
        public RenderTargetIdentifier hiCTexture;
        public RenderTargetIdentifier hiZTexture;
        public RenderTargetIdentifier depthTexture;
        public RenderTargetIdentifier normalTexture;
        public RenderTargetIdentifier motionTexture;
        public RenderTargetIdentifier roughnessTexture;
    }

    public struct SSROutputDescriptor
    {
        public RenderTargetIdentifier reflectionUWVPDF;
        public RenderTargetIdentifier reflectionColorMask;
        public RenderTargetIdentifier reflectionSpatial;
        public RenderTargetIdentifier reflectionHistory;
        public RenderTargetIdentifier reflectionAccmulation;
    }

    public static class SSRShaderIDs
    {
        public static int NumRays = Shader.PropertyToID("SSR_NumRays");
        public static int NumSteps = Shader.PropertyToID("SSR_NumSteps");
        public static int FrameIndex = Shader.PropertyToID("SSR_FrameIndex");
        public static int BRDFBias = Shader.PropertyToID("SSR_BRDFBias");
        public static int Fadeness = Shader.PropertyToID("SSR_Fadeness");
        public static int Roughness = Shader.PropertyToID("SSR_Roughness");
        public static int SpatialRadius = Shader.PropertyToID("SSR_SpatialRadius");
        public static int TemporalScale = Shader.PropertyToID("SSR_TemporalScale");
        public static int TemporalWeight = Shader.PropertyToID("SSR_TemporalWeight");
        public static int Resolution = Shader.PropertyToID("SSR_Resolution");
        public static int FilterResolution = Shader.PropertyToID("SSR_FilterResolution");
        public static int Matrix_Proj = Shader.PropertyToID("Matrix_Proj");
        public static int Matrix_InvProj = Shader.PropertyToID("Matrix_InvProj");
        public static int Matrix_ViewProj = Shader.PropertyToID("Matrix_ViewProj");
        public static int Matrix_InvViewProj = Shader.PropertyToID("Matrix_InvViewProj");
        public static int Matrix_LastViewProj = Shader.PropertyToID("Matrix_LastViewProj");
        public static int Matrix_WorldToView = Shader.PropertyToID("Matrix_WorldToView");
        public static int DepthTexture = Shader.PropertyToID("SRV_DepthTexture");
        public static int MotionTexture = Shader.PropertyToID("SRV_MotionTexture");
        public static int NormalTexture = Shader.PropertyToID("SRV_NormalTexture");
        public static int RoughnessTexture = Shader.PropertyToID("SRV_RoughnessTexture");
        public static int HiCTexture = Shader.PropertyToID("SRV_HiCTexture");
        public static int HiZTexture = Shader.PropertyToID("SRV_HiZTexture");
        public static int HitPDFTextureRead = Shader.PropertyToID("SRV_HitPDFTexture");
        public static int ColorMaskTextureRead = Shader.PropertyToID("SRV_ColorMaskTexture");
        public static int HitPDFTexture = Shader.PropertyToID("UAV_HitPDFTexture");
        public static int ColorMaskTexture = Shader.PropertyToID("UAV_ColorMaskTexture");
        public static int SpatialTexture = Shader.PropertyToID("UAV_SpatialTexture");
        public static int HistoryTexture = Shader.PropertyToID("SRV_HistoryTexture");
        public static int AliasingTexture = Shader.PropertyToID("SRV_AliasingTexture");
        public static int AccmulateTexture = Shader.PropertyToID("UAV_AccmulateTexture");
    }

    public class ScreenSpaceReflectionEffect
    {
        private ComputeShader m_Shader;

        public ScreenSpaceReflectionEffect(ComputeShader shader)
        {
            m_Shader = shader;
        }

        public void Render(CommandBuffer cmdBuffer, in SSRParameterDescriptor parameters, in SSRInputDescriptor inputData, in SSROutputDescriptor outputData) 
        {
            cmdBuffer.SetComputeIntParam(m_Shader, SSRShaderIDs.NumRays, parameters.numRays);
            cmdBuffer.SetComputeIntParam(m_Shader, SSRShaderIDs.NumSteps, parameters.numSteps);
            cmdBuffer.SetComputeIntParam(m_Shader, SSRShaderIDs.FrameIndex, inputData.frameIndex);
            cmdBuffer.SetComputeFloatParam(m_Shader, SSRShaderIDs.BRDFBias, parameters.brdfBias);
            cmdBuffer.SetComputeFloatParam(m_Shader, SSRShaderIDs.Fadeness, parameters.fadeness);
            cmdBuffer.SetComputeFloatParam(m_Shader, SSRShaderIDs.Roughness, parameters.roughness);
            cmdBuffer.SetComputeFloatParam(m_Shader, SSRShaderIDs.SpatialRadius, parameters.spatialRadius);
            cmdBuffer.SetComputeFloatParam(m_Shader, SSRShaderIDs.TemporalScale, parameters.temporalScale);
            cmdBuffer.SetComputeFloatParam(m_Shader, SSRShaderIDs.TemporalWeight, parameters.temporalWeight);
            cmdBuffer.SetComputeVectorParam(m_Shader, SSRShaderIDs.Resolution, inputData.resolution);
            cmdBuffer.SetComputeVectorParam(m_Shader, SSRShaderIDs.FilterResolution, inputData.filterResolution);
            cmdBuffer.SetComputeMatrixParam(m_Shader, SSRShaderIDs.Matrix_Proj, inputData.matrix_Proj);
            cmdBuffer.SetComputeMatrixParam(m_Shader, SSRShaderIDs.Matrix_InvProj, inputData.matrix_InvProj);
            cmdBuffer.SetComputeMatrixParam(m_Shader, SSRShaderIDs.Matrix_ViewProj, inputData.matrix_ViewProj);
            cmdBuffer.SetComputeMatrixParam(m_Shader, SSRShaderIDs.Matrix_InvViewProj, inputData.matrix_InvViewProj);
            cmdBuffer.SetComputeMatrixParam(m_Shader, SSRShaderIDs.Matrix_LastViewProj, inputData.matrix_LastViewProj);
            cmdBuffer.SetComputeMatrixParam(m_Shader, SSRShaderIDs.Matrix_WorldToView, inputData.matrix_WorldToView);

            cmdBuffer.BeginSample("RayTracing");
            cmdBuffer.SetComputeTextureParam(m_Shader, 0, SSRShaderIDs.DepthTexture, inputData.depthTexture);
            cmdBuffer.SetComputeTextureParam(m_Shader, 0, SSRShaderIDs.NormalTexture, inputData.normalTexture);
            cmdBuffer.SetComputeTextureParam(m_Shader, 0, SSRShaderIDs.RoughnessTexture, inputData.roughnessTexture);
            cmdBuffer.SetComputeTextureParam(m_Shader, 0, SSRShaderIDs.HiCTexture, inputData.hiCTexture);
            cmdBuffer.SetComputeTextureParam(m_Shader, 0, SSRShaderIDs.HiZTexture, inputData.hiZTexture);
            cmdBuffer.SetComputeTextureParam(m_Shader, 0, SSRShaderIDs.HitPDFTexture, outputData.reflectionUWVPDF);
            cmdBuffer.SetComputeTextureParam(m_Shader, 0, SSRShaderIDs.ColorMaskTexture, outputData.reflectionColorMask);
            cmdBuffer.DispatchCompute(m_Shader, 0,  Mathf.CeilToInt(inputData.resolution.x / 16),  Mathf.CeilToInt(inputData.resolution.y / 16), 1);
            cmdBuffer.EndSample("RayTracing");

            cmdBuffer.BeginSample("SpatialFilter");
            cmdBuffer.SetComputeTextureParam(m_Shader, 1, SSRShaderIDs.DepthTexture, inputData.depthTexture);
            cmdBuffer.SetComputeTextureParam(m_Shader, 1, SSRShaderIDs.NormalTexture, inputData.normalTexture);
            cmdBuffer.SetComputeTextureParam(m_Shader, 1, SSRShaderIDs.RoughnessTexture, inputData.roughnessTexture);
            cmdBuffer.SetComputeTextureParam(m_Shader, 1, SSRShaderIDs.HitPDFTextureRead, outputData.reflectionUWVPDF);
            for (int i = 0; i < parameters.numSpatial; ++i)
            {
                if ((i & 1) == 0)
                {
                    cmdBuffer.SetComputeTextureParam(m_Shader, 1, SSRShaderIDs.ColorMaskTextureRead, outputData.reflectionColorMask);
                    cmdBuffer.SetComputeTextureParam(m_Shader, 1, SSRShaderIDs.SpatialTexture, outputData.reflectionSpatial);
                    cmdBuffer.DispatchCompute(m_Shader, 1, Mathf.CeilToInt(inputData.filterResolution.x / 16), Mathf.CeilToInt(inputData.filterResolution.y / 16), 1);
                }
                else
                {
                    cmdBuffer.SetComputeTextureParam(m_Shader, 1, SSRShaderIDs.ColorMaskTextureRead, outputData.reflectionSpatial);
                    cmdBuffer.SetComputeTextureParam(m_Shader, 1, SSRShaderIDs.SpatialTexture, outputData.reflectionColorMask);
                    cmdBuffer.DispatchCompute(m_Shader, 1, Mathf.CeilToInt(inputData.filterResolution.x / 16), Mathf.CeilToInt(inputData.filterResolution.y / 16), 1);
                    cmdBuffer.CopyTexture(outputData.reflectionColorMask, outputData.reflectionSpatial);
                }
            }
            cmdBuffer.EndSample("SpatialFilter");

            cmdBuffer.BeginSample("TemporalFilter");
            cmdBuffer.SetComputeTextureParam(m_Shader, 2, SSRShaderIDs.MotionTexture, inputData.motionTexture);
            cmdBuffer.SetComputeTextureParam(m_Shader, 2, SSRShaderIDs.HitPDFTextureRead, outputData.reflectionUWVPDF);
            cmdBuffer.SetComputeTextureParam(m_Shader, 2, SSRShaderIDs.HistoryTexture, outputData.reflectionHistory);
            cmdBuffer.SetComputeTextureParam(m_Shader, 2, SSRShaderIDs.AliasingTexture, outputData.reflectionSpatial);
            cmdBuffer.SetComputeTextureParam(m_Shader, 2, SSRShaderIDs.AccmulateTexture, outputData.reflectionAccmulation);
            cmdBuffer.DispatchCompute(m_Shader, 2, Mathf.CeilToInt(inputData.filterResolution.x / 16), Mathf.CeilToInt(inputData.filterResolution.y / 16), 1);
            cmdBuffer.CopyTexture(outputData.reflectionAccmulation, outputData.reflectionHistory);
            cmdBuffer.EndSample("TemporalFilter");
        }
    }
}