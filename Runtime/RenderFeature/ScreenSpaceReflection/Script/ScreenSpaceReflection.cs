using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace InfinityTech.Runtime.Rendering.Feature
{
    public struct SSRParameterDescriptor
    {
        public int NumRays;
        public int NumSteps;
        public float BRDFBias;
        public float Fadeness;
        public float RoughnessDiscard;
    }

    public struct SSRInputDescriptor
    {
        public int FrameIndex;
        public float4 TraceResolution;
        public float4x4 Matrix_Proj;
        public float4x4 Matrix_InvProj;
        public float4x4 Matrix_InvViewProj;
        public float4x4 Matrix_WorldToView;
        public RenderTargetIdentifier SRV_PyramidColor;
        public RenderTargetIdentifier SRV_PyramidDepth;
        public RenderTargetIdentifier SRV_SceneDepth;
        public RenderTargetIdentifier SRV_GBufferNormal;
        public RenderTargetIdentifier SRV_GBufferRoughness;
    }

    public static class SSRShaderID
    {
        public static int NumRays = Shader.PropertyToID("SSR_NumRays");
        public static int NumSteps = Shader.PropertyToID("SSR_NumSteps");
        public static int FrameIndex = Shader.PropertyToID("SSR_FrameIndex");
        public static int BRDFBias = Shader.PropertyToID("SSR_BRDFBias");
        public static int Fadeness = Shader.PropertyToID("SSR_Fadeness");
        public static int TraceResolution = Shader.PropertyToID("SSR_TraceResolution");
        public static int RoughnessDiscard = Shader.PropertyToID("SSR_RoughnessDiscard");

        public static int Matrix_Proj = Shader.PropertyToID("Matrix_Proj");
        public static int Matrix_InvProj = Shader.PropertyToID("Matrix_InvProj");
        public static int Matrix_InvViewProj = Shader.PropertyToID("Matrix_InvViewProj");
        public static int Matrix_WorldToView = Shader.PropertyToID("Matrix_WorldToView");

        /*public static int SRV_Ranking_Tile = Shader.PropertyToID("SRV_Ranking_Tile");
        public static int SRV_Scrambled_Tile = Shader.PropertyToID("SRV_Scrambled_Tile");
        public static int SRV_Scrambled_Owen = Shader.PropertyToID("SRV_Scrambled_Owen");
        public static int SRV_Scrambled_Noise = Shader.PropertyToID("SRV_Scrambled_Noise");*/

        public static int SRV_PyramidColor = Shader.PropertyToID("SRV_PyramidColor");
        public static int SRV_PyramidDepth = Shader.PropertyToID("SRV_PyramidDepth");
        public static int SRV_SceneDepth = Shader.PropertyToID("SRV_SceneDepth");
        public static int SRV_GBufferNormal = Shader.PropertyToID("SRV_GBufferNormal");
        public static int SRV_GBufferRoughness = Shader.PropertyToID("SRV_GBufferRoughness");
        public static int UAV_ReflectionUVWPDF = Shader.PropertyToID("UAV_ReflectionUWVPDF");
        public static int UAV_ReflectionColorMask = Shader.PropertyToID("UAV_ReflectionColorMask");
    }

    public static class SSR
    {
        /*private static Texture2D Ranking_Tile {
            get {
                return Resources.Load<Texture2D>("Textures/MonteCarlo/RankingTile8SPP");
            }
        }
        private static Texture2D Scrambled_Tile {
            get {
                return Resources.Load<Texture2D>("Textures/MonteCarlo/ScramblingTile8SPP");
            }
        }
        private static Texture2D Scrambled_Owen {
            get {
                return Resources.Load<Texture2D>("Textures/MonteCarlo/ScrambledNoise_Owen");
            }
        }
        private static Texture2D Scrambled_Noise {
            get {
                return Resources.Load<Texture2D>("Textures/MonteCarlo/ScrambledNoise");
            }
        }*/

        private static ComputeShader SSRTraceShader {
            get {
                return Resources.Load<ComputeShader>("Shaders/SSR_TraceShader");
            }
        }
        
        public static void Render(CommandBuffer CmdBuffer, RenderTargetIdentifier UAV_ReflectionUWVPDF, RenderTargetIdentifier UAV_ReflectionColorMask, ref SSRParameterDescriptor Parameters, ref SSRInputDescriptor InputData) {
            CmdBuffer.SetComputeIntParam(SSRTraceShader, SSRShaderID.NumRays, Parameters.NumRays);
            CmdBuffer.SetComputeIntParam(SSRTraceShader, SSRShaderID.NumSteps, Parameters.NumSteps);
            CmdBuffer.SetComputeIntParam(SSRTraceShader, SSRShaderID.FrameIndex, InputData.FrameIndex);

            CmdBuffer.SetComputeFloatParam(SSRTraceShader, SSRShaderID.BRDFBias, Parameters.BRDFBias);
            CmdBuffer.SetComputeFloatParam(SSRTraceShader, SSRShaderID.Fadeness, Parameters.Fadeness);
            CmdBuffer.SetComputeFloatParam(SSRTraceShader, SSRShaderID.RoughnessDiscard, Parameters.RoughnessDiscard);

            CmdBuffer.SetComputeVectorParam(SSRTraceShader, SSRShaderID.TraceResolution, InputData.TraceResolution);

            CmdBuffer.SetComputeMatrixParam(SSRTraceShader, SSRShaderID.Matrix_Proj, InputData.Matrix_Proj);
            CmdBuffer.SetComputeMatrixParam(SSRTraceShader, SSRShaderID.Matrix_InvProj, InputData.Matrix_InvProj);
            CmdBuffer.SetComputeMatrixParam(SSRTraceShader, SSRShaderID.Matrix_InvViewProj, InputData.Matrix_InvViewProj);
            CmdBuffer.SetComputeMatrixParam(SSRTraceShader, SSRShaderID.Matrix_WorldToView, InputData.Matrix_WorldToView);

            CmdBuffer.SetComputeTextureParam(SSRTraceShader, 0, SSRShaderID.SRV_PyramidColor, InputData.SRV_PyramidColor);
            CmdBuffer.SetComputeTextureParam(SSRTraceShader, 0, SSRShaderID.SRV_PyramidDepth, InputData.SRV_PyramidDepth);
            CmdBuffer.SetComputeTextureParam(SSRTraceShader, 0, SSRShaderID.SRV_SceneDepth, InputData.SRV_SceneDepth);
            CmdBuffer.SetComputeTextureParam(SSRTraceShader, 0, SSRShaderID.SRV_GBufferNormal, InputData.SRV_GBufferNormal);
            CmdBuffer.SetComputeTextureParam(SSRTraceShader, 0, SSRShaderID.SRV_GBufferRoughness, InputData.SRV_GBufferRoughness);
            CmdBuffer.SetComputeTextureParam(SSRTraceShader, 0, SSRShaderID.UAV_ReflectionUVWPDF, UAV_ReflectionUWVPDF);
            CmdBuffer.SetComputeTextureParam(SSRTraceShader, 0, SSRShaderID.UAV_ReflectionColorMask, UAV_ReflectionColorMask);

            CmdBuffer.DispatchCompute(SSRTraceShader, 0,  Mathf.CeilToInt(InputData.TraceResolution.x / 16),  Mathf.CeilToInt(InputData.TraceResolution.y / 16), 1);
        }
    }

    /*public static class RTReflection_Trace
    {
        public static void RayTrace_ReflectionTrace(ref RTReflection_TraceParameter ReflectionTraceParameter, ComputeShader RTRComputeShader, CommandBuffer CmdBuffer) {
            CmdBuffer.SetComputeIntParam(RTRComputeShader, RTReflection_TraceUniform.RTR_NumRay, ReflectionTraceParameter.NumRays);
            CmdBuffer.SetComputeIntParam(RTRComputeShader, RTReflection_TraceUniform.SRTR_NumSteps, ReflectionTraceParameter.NumSteps);
            CmdBuffer.SetComputeFloatParam(RTRComputeShader, RTReflection_TraceUniform.RTR_BRDFBias, ReflectionTraceParameter.BRDFBias);
            CmdBuffer.SetComputeFloatParam(RTRComputeShader, RTReflection_TraceUniform.SRTR_MaskFade, ReflectionTraceParameter.Fadeness);
            CmdBuffer.SetComputeFloatParam(RTRComputeShader, RTReflection_TraceUniform.SRTR_Thickness, ReflectionTraceParameter.Thickness);
            CmdBuffer.SetComputeFloatParam(RTRComputeShader, RTReflection_TraceUniform.RTR_RoughnessDiscard, (float)ReflectionTraceParameter.RoughnessDiscard);
            CmdBuffer.SetComputeVectorParam(RTRComputeShader, RTReflection_TraceUniform.RTR_TraceSize, new float4(ReflectionTraceParameter.TraceSize.x, ReflectionTraceParameter.TraceSize.y, 1f / ReflectionTraceParameter.TraceSize.x, 1f / ReflectionTraceParameter.TraceSize.y));
            CmdBuffer.SetComputeTextureParam(RTRComputeShader, 0, RTReflection_TraceUniform.RT_ReflectionUWVPDF, ReflectionTraceParameter.ReflectionUVWPDF);
            CmdBuffer.SetComputeTextureParam(RTRComputeShader, 0, RTReflection_TraceUniform.RT_ReflectionColorMask, ReflectionTraceParameter.ReflectionColorMask);
            CmdBuffer.DispatchCompute(RTRComputeShader, 0,  Mathf.CeilToInt((float)ReflectionTraceParameter.TraceSize.x / 16),  Mathf.CeilToInt((float)ReflectionTraceParameter.TraceSize.y / 16), 1);
        }
    }*/


    ////////////Ray Trace Reflection Denoise
    public struct RTReflection_SpatialParameter
    {
        public int Normalize;
        public int NumSpatial;
        public int SpatialRadius;
        public int2 TraceSize;
        public RenderTargetIdentifier ReflectionUVWPDF;
        public RenderTargetIdentifier ReflectionColorMask;
        public RenderTargetIdentifier ReflectionSpatialColor;
    }

    public static class RTReflection_SpatialUniform
    {
        public static int RT_Normalize = Shader.PropertyToID("F_RTR_Normalize");
        public static int RT_NumSpatial = Shader.PropertyToID("F_RTR_NumSpatial");
        public static int RT_SpatialRadius = Shader.PropertyToID("F_RTR_SpatialRadius");
        public static int RT_TraceSize = Shader.PropertyToID("F_RTR_SpatialSize");
        public static int RT_ReflectionUWVPDF = Shader.PropertyToID("RT_ReflectionUWVPDF");
        public static int RT_ReflectionColorMask = Shader.PropertyToID("RT_ReflectionColorMask");
        public static int RT_ReflectionSpatialColor = Shader.PropertyToID("RT_ReflectionSpatialColor");
    }

    public struct RTReflection_TemporalParameter
    {
        public float TemporalScale;
        public float TemporalWeight;
        public int2 TraceSize;
        public RenderTargetIdentifier ReflectionCurrColor;
        public RenderTargetIdentifier ReflectionPrevColor;
        public RenderTargetIdentifier ReflectionTemporalColor;
    }

    public static class RTReflection_TemporalUniform
    {
        public static int RT_TemporalScale = Shader.PropertyToID("F_RTR_TemporalScale");
        public static int RT_TemporalWeight = Shader.PropertyToID("F_RTR_TemporalWeight");
        public static int RT_TraceSize = Shader.PropertyToID("F_RTR_TemporalSize");
        public static int RT_ReflectionCurrColor = Shader.PropertyToID("RT_ReflectionCurrColor");
        public static int RT_ReflectionPrevColor = Shader.PropertyToID("RT_ReflectionPrevColor");
        public static int RT_ReflectionTemporalColor = Shader.PropertyToID("RT_ReflectionTemporalColor");
    }

    public static class RTReflection_Denoise
    {
        public static void RayTrace_ReflectionSpatial(ref RTReflection_SpatialParameter ReflectionSpatialParameter, ComputeShader RTRComputeShader, CommandBuffer CmdBuffer) {
            CmdBuffer.SetComputeIntParam(RTRComputeShader, RTReflection_SpatialUniform.RT_Normalize, ReflectionSpatialParameter.Normalize);
            CmdBuffer.SetComputeIntParam(RTRComputeShader, RTReflection_SpatialUniform.RT_NumSpatial, ReflectionSpatialParameter.NumSpatial);
            CmdBuffer.SetComputeIntParam(RTRComputeShader, RTReflection_SpatialUniform.RT_SpatialRadius, ReflectionSpatialParameter.SpatialRadius);
            CmdBuffer.SetComputeVectorParam(RTRComputeShader, RTReflection_SpatialUniform.RT_TraceSize, new float4(ReflectionSpatialParameter.TraceSize.x, ReflectionSpatialParameter.TraceSize.y, 1f / ReflectionSpatialParameter.TraceSize.x, 1f / ReflectionSpatialParameter.TraceSize.y));
            CmdBuffer.SetComputeTextureParam(RTRComputeShader, 0, RTReflection_SpatialUniform.RT_ReflectionUWVPDF, ReflectionSpatialParameter.ReflectionUVWPDF);
            CmdBuffer.SetComputeTextureParam(RTRComputeShader, 0, RTReflection_SpatialUniform.RT_ReflectionColorMask, ReflectionSpatialParameter.ReflectionColorMask);
            CmdBuffer.SetComputeTextureParam(RTRComputeShader, 0, RTReflection_SpatialUniform.RT_ReflectionSpatialColor, ReflectionSpatialParameter.ReflectionSpatialColor);
            CmdBuffer.DispatchCompute(RTRComputeShader, 0,  Mathf.CeilToInt((float)ReflectionSpatialParameter.TraceSize.x / 16),  Mathf.CeilToInt((float)ReflectionSpatialParameter.TraceSize.y / 16), 1);
        }

        public static void RayTrace_ReflectionTemporal(ref RTReflection_TemporalParameter ReflectionTemporalParameter, ComputeShader RTRComputeShader, CommandBuffer CmdBuffer) {
            CmdBuffer.SetComputeFloatParam(RTRComputeShader, RTReflection_TemporalUniform.RT_TemporalScale, ReflectionTemporalParameter.TemporalScale);
            CmdBuffer.SetComputeFloatParam(RTRComputeShader, RTReflection_TemporalUniform.RT_TemporalWeight, ReflectionTemporalParameter.TemporalWeight);
            CmdBuffer.SetComputeVectorParam(RTRComputeShader, RTReflection_TemporalUniform.RT_TraceSize, new float4(ReflectionTemporalParameter.TraceSize.x, ReflectionTemporalParameter.TraceSize.y, 1f / ReflectionTemporalParameter.TraceSize.x, 1f / ReflectionTemporalParameter.TraceSize.y));
            CmdBuffer.SetComputeTextureParam(RTRComputeShader, 1, RTReflection_TemporalUniform.RT_ReflectionCurrColor, ReflectionTemporalParameter.ReflectionCurrColor);
            CmdBuffer.SetComputeTextureParam(RTRComputeShader, 1, RTReflection_TemporalUniform.RT_ReflectionPrevColor, ReflectionTemporalParameter.ReflectionPrevColor);
            CmdBuffer.SetComputeTextureParam(RTRComputeShader, 1, RTReflection_TemporalUniform.RT_ReflectionTemporalColor, ReflectionTemporalParameter.ReflectionTemporalColor);
            CmdBuffer.DispatchCompute(RTRComputeShader, 1,  Mathf.CeilToInt((float)ReflectionTemporalParameter.TraceSize.x / 16),  Mathf.CeilToInt((float)ReflectionTemporalParameter.TraceSize.y / 16), 1);
            CmdBuffer.CopyTexture(RTReflection_TemporalUniform.RT_ReflectionTemporalColor, RTReflection_TemporalUniform.RT_ReflectionPrevColor);
        }
    }
}