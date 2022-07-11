using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Feature
{
    public static class HaltonSequence
    {
        public static float Get(int index, in int radix)
        {
            float result = 0f;
            float fraction = 1f / radix;

            while (index > 0) 
            {
                result += (index % radix) * fraction;

                index /= radix;
                fraction /= radix;
            }

            return result;
        }
    }

    public struct TemporalAAParameter
    {
        public float4 blendParameter;

        public TemporalAAParameter(in float staticFactor, in float dynamicFactor, in float motionFactor, in float temporalScale)
        {
            blendParameter = new float4(staticFactor, dynamicFactor, motionFactor, temporalScale);
        }
    }

    public struct TemporalAAInputData
    {
        public float4 resolution;
        public RenderTargetIdentifier depthTexture;
        public RenderTargetIdentifier motionTexture;
        public RenderTargetIdentifier hsitoryTexture;
        public RenderTargetIdentifier aliasingTexture;
    }

    public struct TemporalAAOutputData
    {
        public RenderTargetIdentifier accmulateTexture;
    }

    internal static class TemporalAAShaderID
    {
        public static int Resolution = Shader.PropertyToID("TAA_Resolution");
        public static int BlendParameter = Shader.PropertyToID("TAA_BlendParameter");
        public static int DepthTexture = Shader.PropertyToID("SRV_DepthTexture");
        public static int MotionTexture = Shader.PropertyToID("SRV_MotionTexture");
        public static int HistoryTexture = Shader.PropertyToID("SRV_HistoryTexture");
        public static int AliasingTexture = Shader.PropertyToID("SRV_AliasingTexture");
        public static int AccmulateTexture = Shader.PropertyToID("UAV_AccmulateTexture");
    }

    public sealed class TemporalAntiAliasing
    {
        public void Render(CommandBuffer cmdBuffer, ComputeShader shader, in TemporalAAParameter parameter, in TemporalAAInputData inputData, in TemporalAAOutputData outputData)
        {
            cmdBuffer.SetComputeVectorParam(shader, TemporalAAShaderID.Resolution, inputData.resolution);
            cmdBuffer.SetComputeVectorParam(shader, TemporalAAShaderID.BlendParameter, parameter.blendParameter);
            cmdBuffer.SetComputeTextureParam(shader, 0, TemporalAAShaderID.DepthTexture, inputData.depthTexture);
            cmdBuffer.SetComputeTextureParam(shader, 0, TemporalAAShaderID.MotionTexture, inputData.motionTexture);
            cmdBuffer.SetComputeTextureParam(shader, 0, TemporalAAShaderID.AliasingTexture, inputData.aliasingTexture);
            cmdBuffer.SetComputeTextureParam(shader, 0, TemporalAAShaderID.HistoryTexture, inputData.hsitoryTexture);
            cmdBuffer.SetComputeTextureParam(shader, 0, TemporalAAShaderID.AccmulateTexture, outputData.accmulateTexture);
            cmdBuffer.DispatchCompute(shader, 0, Mathf.CeilToInt(inputData.resolution.x / 16), Mathf.CeilToInt(inputData.resolution.y / 16), 1);
            cmdBuffer.CopyTexture(outputData.accmulateTexture, inputData.hsitoryTexture);
        }

        public static void CaculateProjectionMatrix(Camera view, ref int frameIndex, ref float2 tempJitter, in Matrix4x4 origProj, ref Matrix4x4 proj, ref Matrix4x4 projFlipY)
        {
            float jitterX = HaltonSequence.Get((frameIndex & 1023) + 1, 2) - 0.5f;
            float jitterY = HaltonSequence.Get((frameIndex & 1023) + 1, 3) - 0.5f;
            tempJitter = new float2(jitterX, jitterY);
            tempJitter *= 0.75f;

            if (++frameIndex >= 8)
            {
                frameIndex = 0;
            }

            if (view.orthographic)
            {
                float vertical = view.orthographicSize;
                float horizontal = vertical * view.aspect;

                float2 offset = tempJitter;
                offset.y *= vertical / (0.5f * view.pixelRect.size.y);
                offset.x *= horizontal / (0.5f * view.pixelRect.size.x);

                float left = offset.x - horizontal;
                float right = offset.x + horizontal;
                float top = offset.y + vertical;
                float bottom = offset.y - vertical;

                proj = Matrix4x4.Ortho(left, right, bottom, top, view.nearClipPlane, view.farClipPlane);
                proj = GL.GetGPUProjectionMatrix(proj, true);
                projFlipY = GL.GetGPUProjectionMatrix(proj, false);
            } else {
                var planes = origProj.decomposeProjection;

                float vertFov = math.abs(planes.top) + math.abs(planes.bottom);
                float horizFov = math.abs(planes.left) + math.abs(planes.right);

                var planeJitter = new Vector2(tempJitter.x * horizFov / view.pixelRect.size.x, tempJitter.y * vertFov / view.pixelRect.size.y);

                planes.left += planeJitter.x;
                planes.right += planeJitter.x;
                planes.top += planeJitter.y;
                planes.bottom += planeJitter.y;

                proj = Matrix4x4.Frustum(planes);
                projFlipY = proj;
            }
        }
    }
}
