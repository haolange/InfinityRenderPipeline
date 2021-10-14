using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Feature
{
    public static class FHaltonSequence
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

    public struct FTemporalAAParameter
    {
        public float4 blendParameter;

        public FTemporalAAParameter(in float staticFactor, in float dynamicFactor, in float motionFactor, in float temporalScale)
        {
            blendParameter = new float4(staticFactor, dynamicFactor, motionFactor, temporalScale);
        }
    }

    public struct FTemporalAAInputData
    {
        public float4 resolution;
        public RenderTargetIdentifier depthTexture;
        public RenderTargetIdentifier motionTexture;
        public RenderTargetIdentifier hsitoryTexture;
        public RenderTargetIdentifier aliasingTexture;
    }

    public struct FTemporalAAOutputData
    {
        public RenderTargetIdentifier accmulateTexture;
    }

    internal static class FTemporalAntiAliasingShaderID
    {
        public static int Resolution = Shader.PropertyToID("TAA_Resolution");
        public static int BlendParameter = Shader.PropertyToID("TAA_BlendParameter");
        public static int DepthTexture = Shader.PropertyToID("SRV_DepthTexture");
        public static int MotionTexture = Shader.PropertyToID("SRV_MotionTexture");
        public static int HistoryTexture = Shader.PropertyToID("SRV_HistoryTexture");
        public static int AliasingTexture = Shader.PropertyToID("SRV_AliasingTexture");
        public static int AccmulateTexture = Shader.PropertyToID("UAV_AccmulateTexture");
    }

    public sealed class FTemporalAntiAliasing
    {
        public void Render(CommandBuffer cmdBuffer, ComputeShader shader, in FTemporalAAParameter parameter, in FTemporalAAInputData inputData, in FTemporalAAOutputData outputData)
        {
            cmdBuffer.SetComputeVectorParam(shader, FTemporalAntiAliasingShaderID.Resolution, inputData.resolution);
            cmdBuffer.SetComputeVectorParam(shader, FTemporalAntiAliasingShaderID.BlendParameter, parameter.blendParameter);
            cmdBuffer.SetComputeTextureParam(shader, 0, FTemporalAntiAliasingShaderID.DepthTexture, inputData.depthTexture);
            cmdBuffer.SetComputeTextureParam(shader, 0, FTemporalAntiAliasingShaderID.MotionTexture, inputData.motionTexture);
            cmdBuffer.SetComputeTextureParam(shader, 0, FTemporalAntiAliasingShaderID.AliasingTexture, inputData.aliasingTexture);
            cmdBuffer.SetComputeTextureParam(shader, 0, FTemporalAntiAliasingShaderID.HistoryTexture, inputData.hsitoryTexture);
            cmdBuffer.SetComputeTextureParam(shader, 0, FTemporalAntiAliasingShaderID.AccmulateTexture, outputData.accmulateTexture);
            cmdBuffer.DispatchCompute(shader, 0, Mathf.CeilToInt(inputData.resolution.x / 16), Mathf.CeilToInt(inputData.resolution.y / 16), 1);
            cmdBuffer.CopyTexture(outputData.accmulateTexture, inputData.hsitoryTexture);
        }

        public static Matrix4x4 CaculateProjectionMatrix(Camera view, ref int frameIndex, ref float2 tempJitter, in Matrix4x4 origProj, in bool flipY = true)
        {
            float jitterX = FHaltonSequence.Get((frameIndex & 1023) + 1, 2) - 0.5f;
            float jitterY = FHaltonSequence.Get((frameIndex & 1023) + 1, 3) - 0.5f;
            tempJitter = new float2(jitterX, jitterY);

            if (++frameIndex >= 8)
            {
                frameIndex = 0;
            }

            Matrix4x4 projectionMatrix;

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

                projectionMatrix = Matrix4x4.Ortho(left, right, bottom, top, view.nearClipPlane, view.farClipPlane);
                projectionMatrix = GL.GetGPUProjectionMatrix(projectionMatrix, flipY);
            } else {
                var planes = origProj.decomposeProjection;

                float vertFov = math.abs(planes.top) + math.abs(planes.bottom);
                float horizFov = math.abs(planes.left) + math.abs(planes.right);

                var planeJitter = new Vector2(tempJitter.x * horizFov / view.pixelRect.size.x, tempJitter.y * vertFov / view.pixelRect.size.y);

                planes.left += planeJitter.x;
                planes.right += planeJitter.x;
                planes.top += planeJitter.y;
                planes.bottom += planeJitter.y;

                projectionMatrix = Matrix4x4.Frustum(planes);
            }

            return projectionMatrix;
        }
    }
}
