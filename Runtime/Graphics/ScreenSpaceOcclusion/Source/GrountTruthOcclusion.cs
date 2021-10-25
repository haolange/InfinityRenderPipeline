using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

namespace InfinityTech.Rendering.Feature
{
    public struct FGrountTruthOcclusionParameter
    {
        public int numRay;
        public int numStep;
        public float power;
        public float radius;
        public float intensity;
        public float sharpeness;
        public float temporalScale;
        public float temporalWeight;
    }

    public struct FGrountTruthOcclusionInputData
    {
        public float halfProjScale;
        public float temporalOffset;
        public float temporalDirection;
        public float4 resolution;
        public float4 upsampleSize;
        public float4x4 matrix_Proj;
        public float4x4 matrix_InvProj;
        public float4x4 matrix_ViewProj;
        public float4x4 matrix_InvViewProj;
        public float4x4 matrix_ViewToWorld;
        public float4x4 matrix_WorldToView;
        public RenderTargetIdentifier depthTexture;
        public RenderTargetIdentifier normalTexture;
        public RenderTargetIdentifier motionTexture;
    }

    public struct FGrountTruthOcclusionOutputData
    {
        public RenderTargetIdentifier occlusionTexture;
        public RenderTargetIdentifier spatialTexture;
        public RenderTargetIdentifier upsampleTexture;
        public RenderTargetIdentifier accmulateTexture;
        public RenderTargetIdentifier historyTexture;
    }

    internal struct FGrountTruthOcclusionUnifrom
    {
        public int numRay; 
        public int numStep;
        public float power; 
        public float radius; 
        public float intensity; 
        public float sharpeness;
        public float halfProjScale; 
        public float temporalOffset; 
        public float temporalDirection;
        public float temporalScale;
        public float temporalWeight;
        public float pending;
        public float4 resolution;
        public float4 upsampleSize;
        public float4x4 matrix_Proj; 
        public float4x4 matrix_InvProj; 
        public float4x4 matrix_ViewProj; 
        public float4x4 matrix_InvViewProj; 
        public float4x4 matrix_ViewToWorld; 
        public float4x4 matrix_WorldToView;
    }

    internal static class FGrountTruthOcclusionShaderID
    {
        public static int UnifromData = Shader.PropertyToID("CBV_OcclusionUnifrom");
        public static int DepthTexture = Shader.PropertyToID("SRV_DepthTexture");
        public static int NormalTexture = Shader.PropertyToID("SRV_NormalTexture");
        public static int MotionTexture = Shader.PropertyToID("SRV_MotionTexture");
        public static int OcclusionTexture = Shader.PropertyToID("UAV_OcclusionTexture");
        public static int OcclusionTextureRead = Shader.PropertyToID("SRV_OcclusionTexture");
        public static int SpatialTexture = Shader.PropertyToID("UAV_SpatialTexture");
        public static int HistoryTexture = Shader.PropertyToID("SRV_HistoryTexture");
        public static int AccmulateTexture = Shader.PropertyToID("UAV_AccmulateTexture");
        public static int UpsampleTexture = Shader.PropertyToID("UAV_UpsampleTexture");
    }

    public sealed class FGrountTruthOcclusion
    {
        private int m_UnifromStride;
        private ComputeBuffer m_OcclusionUnifrom;
        private FGrountTruthOcclusionUnifrom[] m_UnifromData;
        private static readonly float[] s_SpatialOffsets = {0, 0.5f, 0.25f, 0.75f};
	    private static readonly float[] s_TemporalRotations = {60, 300, 180, 240, 120, 0};

        public FGrountTruthOcclusion()
        {
            m_UnifromData = new FGrountTruthOcclusionUnifrom[1];
            m_UnifromStride = Marshal.SizeOf(typeof(FGrountTruthOcclusionUnifrom));
            m_OcclusionUnifrom = new ComputeBuffer(1, m_UnifromStride);
        }

        public void CaculateJitterInfo(in int frameIndex, ref float temporalOffset, ref float temporalRotation)
        {
            temporalRotation = s_TemporalRotations[frameIndex % 6];
            temporalRotation /= 360;
            temporalOffset = s_SpatialOffsets[(frameIndex / 6) % 4];
        }

        void UpdateUnifromData(in FGrountTruthOcclusionParameter parameter, in FGrountTruthOcclusionInputData inputData) 
        {
            m_UnifromData[0].numRay = parameter.numRay; 
            m_UnifromData[0].numStep = parameter.numStep;
            m_UnifromData[0].power = parameter.power; 
            m_UnifromData[0].radius = parameter.radius; 
            m_UnifromData[0].intensity = parameter.intensity; 
            m_UnifromData[0].sharpeness = parameter.sharpeness;
            m_UnifromData[0].halfProjScale = inputData.halfProjScale; 
            m_UnifromData[0].temporalOffset = inputData.temporalOffset; 
            m_UnifromData[0].temporalDirection = inputData.temporalDirection;
            m_UnifromData[0].temporalScale = parameter.temporalScale;
            m_UnifromData[0].temporalWeight = parameter.temporalWeight;
            m_UnifromData[0].pending = 0;
            m_UnifromData[0].resolution = inputData.resolution;
            m_UnifromData[0].upsampleSize = inputData.upsampleSize;
            m_UnifromData[0].matrix_Proj = inputData.matrix_Proj; 
            m_UnifromData[0].matrix_InvProj = inputData.matrix_InvProj; 
            m_UnifromData[0].matrix_ViewProj = inputData.matrix_ViewProj; 
            m_UnifromData[0].matrix_InvViewProj = inputData.matrix_InvViewProj; 
            m_UnifromData[0].matrix_ViewToWorld = inputData.matrix_ViewToWorld; 
            m_UnifromData[0].matrix_WorldToView = inputData.matrix_WorldToView;
            m_OcclusionUnifrom.SetData(m_UnifromData);
        }

        public void Render(CommandBuffer cmdBuffer, ComputeShader shader, in FGrountTruthOcclusionParameter parameter, in FGrountTruthOcclusionInputData inputData, in FGrountTruthOcclusionOutputData outoutData) 
        {
            UpdateUnifromData(parameter, inputData);
            cmdBuffer.SetComputeConstantBufferParam(shader, FGrountTruthOcclusionShaderID.UnifromData, m_OcclusionUnifrom, 0, m_UnifromStride);

            cmdBuffer.BeginSample("RayMarch");
            cmdBuffer.SetComputeTextureParam(shader, 0, FGrountTruthOcclusionShaderID.DepthTexture, inputData.depthTexture);
            cmdBuffer.SetComputeTextureParam(shader, 0, FGrountTruthOcclusionShaderID.NormalTexture, inputData.normalTexture);
            cmdBuffer.SetComputeTextureParam(shader, 0, FGrountTruthOcclusionShaderID.OcclusionTexture, outoutData.occlusionTexture);
            cmdBuffer.DispatchCompute(shader, 0,  Mathf.CeilToInt(inputData.resolution.x / 16),  Mathf.CeilToInt(inputData.resolution.y / 16), 1);
            cmdBuffer.EndSample("RayMarch");

            cmdBuffer.BeginSample("SpatialX");
            cmdBuffer.SetComputeTextureParam(shader, 1, FGrountTruthOcclusionShaderID.DepthTexture, inputData.depthTexture);
            cmdBuffer.SetComputeTextureParam(shader, 1, FGrountTruthOcclusionShaderID.SpatialTexture, outoutData.spatialTexture);
            cmdBuffer.SetComputeTextureParam(shader, 1, FGrountTruthOcclusionShaderID.OcclusionTextureRead, outoutData.occlusionTexture);
            cmdBuffer.DispatchCompute(shader, 1, Mathf.CeilToInt(inputData.resolution.x / 16), Mathf.CeilToInt(inputData.resolution.y / 16), 1);
            cmdBuffer.EndSample("SpatialX");

            cmdBuffer.BeginSample("SpatialY");
            cmdBuffer.SetComputeTextureParam(shader, 2, FGrountTruthOcclusionShaderID.DepthTexture, inputData.depthTexture);
            cmdBuffer.SetComputeTextureParam(shader, 2, FGrountTruthOcclusionShaderID.SpatialTexture, outoutData.occlusionTexture);
            cmdBuffer.SetComputeTextureParam(shader, 2, FGrountTruthOcclusionShaderID.OcclusionTextureRead, outoutData.spatialTexture);
            cmdBuffer.DispatchCompute(shader, 2, Mathf.CeilToInt(inputData.resolution.x / 16), Mathf.CeilToInt(inputData.resolution.y / 16), 1);
            cmdBuffer.EndSample("SpatialY");

            cmdBuffer.BeginSample("Temporal");
            cmdBuffer.SetComputeTextureParam(shader, 3, FGrountTruthOcclusionShaderID.MotionTexture, inputData.motionTexture);
            cmdBuffer.SetComputeTextureParam(shader, 3, FGrountTruthOcclusionShaderID.AccmulateTexture, outoutData.accmulateTexture);
            cmdBuffer.SetComputeTextureParam(shader, 3, FGrountTruthOcclusionShaderID.OcclusionTextureRead, outoutData.occlusionTexture);
            cmdBuffer.SetComputeTextureParam(shader, 3, FGrountTruthOcclusionShaderID.HistoryTexture, outoutData.historyTexture);
            cmdBuffer.DispatchCompute(shader, 3, Mathf.CeilToInt(inputData.upsampleSize.x / 16), Mathf.CeilToInt(inputData.upsampleSize.y / 16), 1);
            cmdBuffer.CopyTexture(outoutData.accmulateTexture, outoutData.historyTexture);
            cmdBuffer.EndSample("Temporal");
        }

        public void Release()
        {
            m_OcclusionUnifrom.Dispose();
        }
    }
}




            /*cmdBuffer.BeginSample("Upsample");
            cmdBuffer.SetComputeTextureParam(shader, 3, FGrountTruthOcclusionShaderID.DepthTexture, inputData.depthTexture);
            cmdBuffer.SetComputeTextureParam(shader, 3, FGrountTruthOcclusionShaderID.NormalTexture, inputData.normalTexture);
            cmdBuffer.SetComputeTextureParam(shader, 3, FGrountTruthOcclusionShaderID.OcclusionTextureRead, outoutData.occlusionTexture);
            cmdBuffer.SetComputeTextureParam(shader, 3, FGrountTruthOcclusionShaderID.UpsampleTexture, outoutData.upsampleTexture);
            cmdBuffer.DispatchCompute(shader, 3, Mathf.CeilToInt(inputData.upsampleSize.x / 16), Mathf.CeilToInt(inputData.upsampleSize.y / 16), 1);
            cmdBuffer.EndSample("Upsample");

            cmdBuffer.BeginSample("Temporal");
            cmdBuffer.SetComputeTextureParam(shader, 4, FGrountTruthOcclusionShaderID.MotionTexture, inputData.motionTexture);
            cmdBuffer.SetComputeTextureParam(shader, 4, FGrountTruthOcclusionShaderID.AccmulateTexture, outoutData.accmulateTexture);
            cmdBuffer.SetComputeTextureParam(shader, 4, FGrountTruthOcclusionShaderID.OcclusionTextureRead, outoutData.upsampleTexture);
            cmdBuffer.SetComputeTextureParam(shader, 4, FGrountTruthOcclusionShaderID.HistoryTexture, outoutData.historyTexture);
            cmdBuffer.DispatchCompute(shader, 4, Mathf.CeilToInt(inputData.upsampleSize.x / 16), Mathf.CeilToInt(inputData.upsampleSize.y / 16), 1);
            cmdBuffer.CopyTexture(outoutData.accmulateTexture, outoutData.historyTexture);
            cmdBuffer.EndSample("Temporal");*/
