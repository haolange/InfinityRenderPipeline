using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Runtime.Rendering.Feature
{
    public struct RTAOParameter {
        public int NumRays;
        public int FrameIndex;
        public float Radius;
    }

    public struct RTAOInputData {
        public float4 TraceResolution;
        public float4x4 Matrix_Proj;
        public float4x4 Matrix_InvProj;
        public float4x4 Matrix_InvViewProj;
        public float4x4 Matrix_WorldToView;
        public RenderTargetIdentifier SRV_SceneDepth;
        public RenderTargetIdentifier SRV_GBufferNormal;
    }

    public static class RTAOShaderID
    {
        public static int NumRays = Shader.PropertyToID("RTAO_NumRays");
        public static int Radius = Shader.PropertyToID("RTAO_Radius");
        public static int FrameIndex = Shader.PropertyToID("RTAO_FrameIndex");
        public static int TraceResolution = Shader.PropertyToID("RTAO_TraceResolution");

        public static int Matrix_Proj = Shader.PropertyToID("Matrix_Proj");
        public static int Matrix_InvProj = Shader.PropertyToID("Matrix_InvProj");
        public static int Matrix_InvViewProj = Shader.PropertyToID("Matrix_InvViewProj");
        public static int Matrix_WorldToView = Shader.PropertyToID("Matrix_WorldToView");

        public static int SRV_SceneDepth = Shader.PropertyToID("SRV_SceneDepth");
        public static int SRV_GBufferNormal = Shader.PropertyToID("SRV_GBufferNormal");
        public static int UAV_ScreenOcclusion = Shader.PropertyToID("UAV_ScreenOcclusion");
    }
    public static class RTAO
    {
        private static string RTAOPassID = "RayTraceAmbientOcclusion";
        private static string RTAOGenerayID = "RTAO_Generation";
        private static string SceneStructID = "_RaytracingSceneStruct";
        public static int UAV_ScreenOcclusion = Shader.PropertyToID("UAV_ScreenOcclusion");

        private static RayTracingShader RTAO_Shader {
            get {
                return Resources.Load<RayTracingShader>("Shaders/RTAO_Shader");
            }
        }

        public static void BindSceneStruct(CommandBuffer CmdBuffer, RayTracingAccelerationStructure RTSceneStruct) {
            CmdBuffer.SetRayTracingShaderPass(RTAO_Shader, RTAOPassID);
            CmdBuffer.SetRayTracingAccelerationStructure(RTAO_Shader, SceneStructID, RTSceneStruct);
        }

        public static void Render(Camera RenderCamera, CommandBuffer CmdBuffer, ref RenderTargetIdentifier UAV_ScreenOcclusion, ref RTAOParameter Parameters, ref RTAOInputData InputData) {
            CmdBuffer.SetRayTracingIntParam(RTAO_Shader, RTAOShaderID.NumRays, Parameters.NumRays);
            CmdBuffer.SetRayTracingIntParam(RTAO_Shader, RTAOShaderID.FrameIndex, Parameters.FrameIndex);

            CmdBuffer.SetRayTracingFloatParam(RTAO_Shader, RTAOShaderID.Radius, Parameters.Radius);

            CmdBuffer.SetRayTracingVectorParam(RTAO_Shader, RTAOShaderID.TraceResolution, InputData.TraceResolution);

            CmdBuffer.SetRayTracingMatrixParam(RTAO_Shader, RTAOShaderID.Matrix_Proj, InputData.Matrix_Proj);
            CmdBuffer.SetRayTracingMatrixParam(RTAO_Shader, RTAOShaderID.Matrix_InvProj, InputData.Matrix_InvProj);
            CmdBuffer.SetRayTracingMatrixParam(RTAO_Shader, RTAOShaderID.Matrix_InvViewProj, InputData.Matrix_InvViewProj);
            CmdBuffer.SetRayTracingMatrixParam(RTAO_Shader, RTAOShaderID.Matrix_WorldToView, InputData.Matrix_WorldToView);

            CmdBuffer.SetRayTracingTextureParam(RTAO_Shader, RTAOShaderID.SRV_SceneDepth, InputData.SRV_SceneDepth);
            CmdBuffer.SetRayTracingTextureParam(RTAO_Shader, RTAOShaderID.SRV_GBufferNormal, InputData.SRV_GBufferNormal);
            CmdBuffer.SetRayTracingTextureParam(RTAO_Shader, RTAOShaderID.UAV_ScreenOcclusion, UAV_ScreenOcclusion);

            CmdBuffer.DispatchRays(RTAO_Shader, RTAOGenerayID, (uint)InputData.TraceResolution.x,  (uint)InputData.TraceResolution.y, 1, RenderCamera);
        }       
    }
}
