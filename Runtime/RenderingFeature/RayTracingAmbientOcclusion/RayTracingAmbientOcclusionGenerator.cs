using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Feature
{
    public struct RayTracingOcclusionParameter
    {
        public int numRays;
        public float radius;
    }

    public struct RayTracingOcclusionInputData
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

    public struct RayTracingOcclusionOuputData
    {
        public RenderTargetIdentifier screenOcclusion;
    }

    internal static class RayTracingOcclusionShaderID
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
    public sealed class RayTracingAmbientOcclusionGenerator
    {
        private static string KernelID = "RayGeneration";
        private static string RayTraceSceneID = "_RaytracingSceneStruct";
        private static string RayTraceAOPassID = "RayTraceAmbientOcclusion";

        private RayTracingShader m_Shader;

        public RayTracingAmbientOcclusionGenerator(RayTracingShader shader)
        {
            this.m_Shader = shader;
        }

        public void BindSceneStruct<T>(T cmd, RayTracingAccelerationStructure rayTraceScene) where T : IRaytracingCommands
        {
            cmd.SetRayTracingShaderPass(m_Shader, RayTraceAOPassID);
            cmd.SetRayTracingAccelerationStructure(m_Shader, RayTraceSceneID, rayTraceScene);
        }

        public void Render<T>(Camera camera, T cmd, in RayTracingOcclusionParameter parameter, in RayTracingOcclusionInputData inputData, in RayTracingOcclusionOuputData outputData) where T : IRaytracingCommands
        {
            cmd.SetRayTracingIntParam(m_Shader, RayTracingOcclusionShaderID.NumRays, parameter.numRays);
            cmd.SetRayTracingIntParam(m_Shader, RayTracingOcclusionShaderID.FrameIndex, inputData.frameIndex);
            cmd.SetRayTracingFloatParam(m_Shader, RayTracingOcclusionShaderID.Radius, parameter.radius);
            cmd.SetRayTracingVectorParam(m_Shader, RayTracingOcclusionShaderID.Resolution, inputData.resolution);
            cmd.SetRayTracingMatrixParam(m_Shader, RayTracingOcclusionShaderID.Matrix_Proj, inputData.matrix_Proj);
            cmd.SetRayTracingMatrixParam(m_Shader, RayTracingOcclusionShaderID.Matrix_InvProj, inputData.matrix_InvProj);
            cmd.SetRayTracingMatrixParam(m_Shader, RayTracingOcclusionShaderID.Matrix_InvViewProj, inputData.matrix_InvViewProj);
            cmd.SetRayTracingMatrixParam(m_Shader, RayTracingOcclusionShaderID.Matrix_WorldToView, inputData.matrix_WorldToView);
            cmd.SetRayTracingTextureParam(m_Shader, RayTracingOcclusionShaderID.SceneDepth, inputData.sceneDepth);
            cmd.SetRayTracingTextureParam(m_Shader, RayTracingOcclusionShaderID.GBufferNormal, inputData.gBufferNormal);
            cmd.SetRayTracingTextureParam(m_Shader, RayTracingOcclusionShaderID.ScreenOcclusion, outputData.screenOcclusion);
            cmd.DispatchRays(m_Shader, KernelID, (uint)inputData.resolution.x,  (uint)inputData.resolution.y, 1, camera);
        }       
    }
}
