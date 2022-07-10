using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GraphicsFeature
{
    public struct SSGiParameterDescriptor
    {
        public int numRays;
        public int numSteps;
        public float intensity;
    }

    public struct SSGiInputDescriptor
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
    }

    public struct SSGiOutputDescriptor
    {
        public RenderTargetIdentifier irradianceColor;
    }

    public static class SSGiShaderID
    {
        public static int NumRays = Shader.PropertyToID("SSGi_NumRays");
        public static int NumSteps = Shader.PropertyToID("SSGi_NumSteps");
        public static int FrameIndex = Shader.PropertyToID("SSGi_FrameIndex");
        public static int Intensity = Shader.PropertyToID("SSGi_Intensity");
        public static int TraceResolution = Shader.PropertyToID("SSGi_TraceResolution");
        public static int UAV_ScreenIrradiance = Shader.PropertyToID("UAV_ScreenIrradiance");

        public static int Matrix_Proj = Shader.PropertyToID("Matrix_Proj");
        public static int Matrix_InvProj = Shader.PropertyToID("Matrix_InvProj");
        public static int Matrix_ViewProj = Shader.PropertyToID("Matrix_ViewProj");
        public static int Matrix_InvViewProj = Shader.PropertyToID("Matrix_InvViewProj");
        public static int Matrix_WorldToView = Shader.PropertyToID("Matrix_WorldToView");

        public static int SRV_HiCTexture = Shader.PropertyToID("SRV_PyramidColor");
        public static int SRV_HiZTexture = Shader.PropertyToID("SRV_PyramidDepth");
        public static int SRV_SceneDepth = Shader.PropertyToID("SRV_SceneDepth");
        public static int SRV_GBufferNormal = Shader.PropertyToID("SRV_GBufferNormal");
    }

    public class ScreenSpaceIndirectEffect
    {
        private ComputeShader m_Shader;

        public ScreenSpaceIndirectEffect(ComputeShader shader)
        {
            m_Shader = shader;
        }

        public void Render(CommandBuffer CmdBuffer, in SSGiParameterDescriptor parameters, in SSGiInputDescriptor inputData, in SSGiOutputDescriptor outputData) 
        {
            CmdBuffer.SetComputeIntParam(m_Shader, SSGiShaderID.NumRays, parameters.numRays);
            CmdBuffer.SetComputeIntParam(m_Shader, SSGiShaderID.NumSteps, parameters.numSteps);
            CmdBuffer.SetComputeIntParam(m_Shader, SSGiShaderID.FrameIndex, inputData.frameIndex);
            CmdBuffer.SetComputeFloatParam(m_Shader, SSGiShaderID.Intensity, parameters.intensity);
            CmdBuffer.SetComputeVectorParam(m_Shader, SSGiShaderID.TraceResolution, inputData.resolution);

            CmdBuffer.SetComputeMatrixParam(m_Shader, SSGiShaderID.Matrix_Proj, inputData.matrix_Proj);
            CmdBuffer.SetComputeMatrixParam(m_Shader, SSGiShaderID.Matrix_InvProj, inputData.matrix_InvProj);
            CmdBuffer.SetComputeMatrixParam(m_Shader, SSGiShaderID.Matrix_ViewProj, inputData.matrix_ViewProj);
            CmdBuffer.SetComputeMatrixParam(m_Shader, SSGiShaderID.Matrix_InvViewProj, inputData.matrix_InvViewProj);
            CmdBuffer.SetComputeMatrixParam(m_Shader, SSGiShaderID.Matrix_WorldToView, inputData.matrix_WorldToView);

            CmdBuffer.SetComputeTextureParam(m_Shader, 0, SSGiShaderID.SRV_HiCTexture, inputData.hiCTexture);
            CmdBuffer.SetComputeTextureParam(m_Shader, 0, SSGiShaderID.SRV_HiZTexture, inputData.hiZTexture);
            CmdBuffer.SetComputeTextureParam(m_Shader, 0, SSGiShaderID.SRV_SceneDepth, inputData.depthTexture);
            CmdBuffer.SetComputeTextureParam(m_Shader, 0, SSGiShaderID.SRV_GBufferNormal, inputData.normalTexture);
            CmdBuffer.SetComputeTextureParam(m_Shader, 0, SSGiShaderID.UAV_ScreenIrradiance, outputData.irradianceColor);

            CmdBuffer.DispatchCompute(m_Shader, 0,  Mathf.CeilToInt(inputData.resolution.x / 16),  Mathf.CeilToInt(inputData.resolution.y / 16), 1);
        }
    }
}