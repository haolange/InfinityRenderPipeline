using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Feature
{
    public struct FRayTracingOcclusionParameter
    {
        public int numRays;
        public float radius;
    }

    public struct FRayTracingOcclusionInputData
    {
        public int frameIndex;
        public float4 resolution;
        public float4x4 matrix_Proj;
        public float4x4 matrix_InvProj;
        public float4x4 matrix_InvViewProj;
        public float4x4 matrix_WorldToView;
        public RenderTargetIdentifier sceneDepth;
        public RenderTargetIdentifier gBufferNormal;
    }

    public struct FRayTracingOcclusionOuputData
    {
        public RenderTargetIdentifier screenOcclusion;
    }

    internal static class FRayTracingOcclusionShaderID
    {
        public static int NumRays = Shader.PropertyToID("RTAO_NumRays");
        public static int Radius = Shader.PropertyToID("RTAO_Radius");
        public static int FrameIndex = Shader.PropertyToID("RTAO_FrameIndex");
        public static int Resolution = Shader.PropertyToID("RTAO_Resolution");

        public static int Matrix_Proj = Shader.PropertyToID("Matrix_Proj");
        public static int Matrix_InvProj = Shader.PropertyToID("Matrix_InvProj");
        public static int Matrix_InvViewProj = Shader.PropertyToID("Matrix_InvViewProj");
        public static int Matrix_WorldToView = Shader.PropertyToID("Matrix_WorldToView");

        public static int SceneDepth = Shader.PropertyToID("SRV_SceneDepth");
        public static int GBufferNormal = Shader.PropertyToID("SRV_GBufferNormal");
        public static int ScreenOcclusion = Shader.PropertyToID("UAV_ScreenOcclusion");
    }
    public sealed class FRayTracingOcclusion
    {
        private static string KernelID = "RayGeneration";
        private static string RayTraceSceneID = "_RaytracingSceneStruct";
        private static string RayTraceAOPassID = "RayTraceAmbientOcclusion";

        private RayTracingShader m_Shader;

        public FRayTracingOcclusion(RayTracingShader shader)
        {
            this.m_Shader = shader;
        }

        public void BindSceneStruct(CommandBuffer cmdBuffer, RayTracingAccelerationStructure rayTraceScene) 
        {
            cmdBuffer.SetRayTracingShaderPass(m_Shader, RayTraceAOPassID);
            cmdBuffer.SetRayTracingAccelerationStructure(m_Shader, RayTraceSceneID, rayTraceScene);
        }

        public void Render(Camera camera, CommandBuffer cmdBuffer, in FRayTracingOcclusionParameter parameter, in FRayTracingOcclusionInputData inputData, in FRayTracingOcclusionOuputData outputData) 
        {
            cmdBuffer.SetRayTracingIntParam(m_Shader, FRayTracingOcclusionShaderID.NumRays, parameter.numRays);
            cmdBuffer.SetRayTracingIntParam(m_Shader, FRayTracingOcclusionShaderID.FrameIndex, inputData.frameIndex);
            cmdBuffer.SetRayTracingFloatParam(m_Shader, FRayTracingOcclusionShaderID.Radius, parameter.radius);
            cmdBuffer.SetRayTracingVectorParam(m_Shader, FRayTracingOcclusionShaderID.Resolution, inputData.resolution);
            cmdBuffer.SetRayTracingMatrixParam(m_Shader, FRayTracingOcclusionShaderID.Matrix_Proj, inputData.matrix_Proj);
            cmdBuffer.SetRayTracingMatrixParam(m_Shader, FRayTracingOcclusionShaderID.Matrix_InvProj, inputData.matrix_InvProj);
            cmdBuffer.SetRayTracingMatrixParam(m_Shader, FRayTracingOcclusionShaderID.Matrix_InvViewProj, inputData.matrix_InvViewProj);
            cmdBuffer.SetRayTracingMatrixParam(m_Shader, FRayTracingOcclusionShaderID.Matrix_WorldToView, inputData.matrix_WorldToView);
            cmdBuffer.SetRayTracingTextureParam(m_Shader, FRayTracingOcclusionShaderID.SceneDepth, inputData.sceneDepth);
            cmdBuffer.SetRayTracingTextureParam(m_Shader, FRayTracingOcclusionShaderID.GBufferNormal, inputData.gBufferNormal);
            cmdBuffer.SetRayTracingTextureParam(m_Shader, FRayTracingOcclusionShaderID.ScreenOcclusion, outputData.screenOcclusion);
            cmdBuffer.DispatchRays(m_Shader, KernelID, (uint)inputData.resolution.x,  (uint)inputData.resolution.y, 1, camera);
        }       
    }
}
