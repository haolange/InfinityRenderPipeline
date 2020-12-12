using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace InfinityTech.Runtime.Rendering.Feature
{
    public struct SSGiParameterDescriptor
    {
        public bool RayMask;
        public int NumRays;
        public int NumSteps;
        public float Thickness;
        public float Intensity;
    }

    public struct SSGiInputDescriptor
    {
        public int FrameIndex;
        public float4 TraceResolution;
        public float4x4 Matrix_Proj;
        public float4x4 Matrix_InvProj;
        public float4x4 Matrix_ViewProj;
        public float4x4 Matrix_InvViewProj;
        public float4x4 Matrix_WorldToView;
        public RenderTargetIdentifier SRV_PyramidColor;
        public RenderTargetIdentifier SRV_PyramidDepth;
        public RenderTargetIdentifier SRV_SceneDepth;
        public RenderTargetIdentifier SRV_GBufferNormal;
    }

    public static class SSGiShaderID
    {
        public static int NumRays = Shader.PropertyToID("SSGi_NumRays");
        public static int NumSteps = Shader.PropertyToID("SSGi_NumSteps");
        public static int RayMask = Shader.PropertyToID("SSGi_RayMask");
        public static int FrameIndex = Shader.PropertyToID("SSGi_FrameIndex");
        public static int Thickness = Shader.PropertyToID("SSGi_Thickness");
        public static int Intensity = Shader.PropertyToID("SSGi_Intensity");
        public static int TraceResolution = Shader.PropertyToID("SSGi_TraceResolution");
        public static int UAV_ScreenIrradiance = Shader.PropertyToID("UAV_ScreenIrradiance");

        public static int Matrix_Proj = Shader.PropertyToID("Matrix_Proj");
        public static int Matrix_InvProj = Shader.PropertyToID("Matrix_InvProj");
        public static int Matrix_ViewProj = Shader.PropertyToID("Matrix_ViewProj");
        public static int Matrix_InvViewProj = Shader.PropertyToID("Matrix_InvViewProj");
        public static int Matrix_WorldToView = Shader.PropertyToID("Matrix_WorldToView");

        public static int SRV_PyramidColor = Shader.PropertyToID("SRV_PyramidColor");
        public static int SRV_PyramidDepth = Shader.PropertyToID("SRV_PyramidDepth");
        public static int SRV_SceneDepth = Shader.PropertyToID("SRV_SceneDepth");
        public static int SRV_GBufferNormal = Shader.PropertyToID("SRV_GBufferNormal");
    }

    public static class SSGi 
    {
        private static ComputeShader SSGiComputeShader {
            get {
                return Resources.Load<ComputeShader>("Shaders/SSGi_TraceShader");
            }
        }

        public static void Render(CommandBuffer CmdBuffer, RenderTargetIdentifier UAV_ScreenIrradiance, ref SSGiParameterDescriptor Parameters, ref SSGiInputDescriptor InputData) {
            CmdBuffer.SetComputeIntParam(SSGiComputeShader, SSGiShaderID.NumRays, Parameters.NumRays);
            CmdBuffer.SetComputeIntParam(SSGiComputeShader, SSGiShaderID.NumSteps, Parameters.NumSteps);
            CmdBuffer.SetComputeIntParam(SSGiComputeShader, SSGiShaderID.RayMask, (Parameters.RayMask == true)? 1 : 0);
            CmdBuffer.SetComputeIntParam(SSGiComputeShader, SSGiShaderID.FrameIndex, InputData.FrameIndex);
            CmdBuffer.SetComputeFloatParam(SSGiComputeShader, SSGiShaderID.Thickness, Parameters.Thickness);
            CmdBuffer.SetComputeFloatParam(SSGiComputeShader, SSGiShaderID.Intensity, Parameters.Intensity);
            CmdBuffer.SetComputeVectorParam(SSGiComputeShader, SSGiShaderID.TraceResolution, InputData.TraceResolution);

            CmdBuffer.SetComputeMatrixParam(SSGiComputeShader, SSGiShaderID.Matrix_Proj, InputData.Matrix_Proj);
            CmdBuffer.SetComputeMatrixParam(SSGiComputeShader, SSGiShaderID.Matrix_InvProj, InputData.Matrix_InvProj);
            CmdBuffer.SetComputeMatrixParam(SSGiComputeShader, SSGiShaderID.Matrix_ViewProj, InputData.Matrix_ViewProj);
            CmdBuffer.SetComputeMatrixParam(SSGiComputeShader, SSGiShaderID.Matrix_InvViewProj, InputData.Matrix_InvViewProj);
            CmdBuffer.SetComputeMatrixParam(SSGiComputeShader, SSGiShaderID.Matrix_WorldToView, InputData.Matrix_WorldToView);

            CmdBuffer.SetComputeTextureParam(SSGiComputeShader, 0, SSGiShaderID.SRV_PyramidColor, InputData.SRV_PyramidColor);
            CmdBuffer.SetComputeTextureParam(SSGiComputeShader, 0, SSGiShaderID.SRV_PyramidDepth, InputData.SRV_PyramidDepth);
            CmdBuffer.SetComputeTextureParam(SSGiComputeShader, 0, SSGiShaderID.SRV_SceneDepth, InputData.SRV_SceneDepth);
            CmdBuffer.SetComputeTextureParam(SSGiComputeShader, 0, SSGiShaderID.SRV_GBufferNormal, InputData.SRV_GBufferNormal);
            CmdBuffer.SetComputeTextureParam(SSGiComputeShader, 0, SSGiShaderID.UAV_ScreenIrradiance, UAV_ScreenIrradiance);

            CmdBuffer.DispatchCompute(SSGiComputeShader, 0,  Mathf.CeilToInt(InputData.TraceResolution.x / 16),  Mathf.CeilToInt(InputData.TraceResolution.y / 16), 1);
        }
    }
}