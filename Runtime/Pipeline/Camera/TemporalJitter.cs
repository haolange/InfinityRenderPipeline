using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;


namespace InfinityTech.Rendering.Pipeline
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

    internal static class TemporalJitter
    {
        // Clip-space offset of the jittered projection in UV units, measured at the view axis.
        internal static Vector2 ProjectionJitterUV(Matrix4x4 jittered, Matrix4x4 unjittered)
        {
            Vector4 point = new Vector4(0, 0, -1, 1);
            Vector4 a = jittered * point, b = unjittered * point;
            return new Vector2(a.x / a.w - b.x / b.w, a.y / a.w - b.y / b.w) * 0.5f;
        }

        public static void GetJitteredPerspectiveProjectionMatrix(Camera camera, int2 renderSize, float2 offset, ref Matrix4x4 proj, ref Matrix4x4 projFlipY)
        {
            float near = camera.nearClipPlane;
            float far = camera.farClipPlane;

            float vertical = Mathf.Tan(0.5f * Mathf.Deg2Rad * camera.fieldOfView) * near;
            float horizontal = vertical * camera.aspect;

            offset.x *= horizontal / (0.5f * renderSize.x);
            offset.y *= vertical / (0.5f * renderSize.y);

            proj = camera.projectionMatrix;

            proj[0, 2] += offset.x / horizontal;
            proj[1, 2] += offset.y / vertical;

            Matrix4x4 jitteredProj = proj;
            proj = GL.GetGPUProjectionMatrix(jitteredProj, true);
            projFlipY = GL.GetGPUProjectionMatrix(jitteredProj, false);
        }

        public static void GetJitteredOrthographicProjectionMatrix(Camera camera, int2 renderSize, float2 offset, ref Matrix4x4 proj, ref Matrix4x4 projFlipY)
        {
            Matrix4x4 jitteredProj = camera.projectionMatrix;
            float x = 2.0f * offset.x / renderSize.x;
            float y = 2.0f * offset.y / renderSize.y;
            for (int column = 0; column < 4; ++column)
            {
                jitteredProj[0, column] -= x * jitteredProj[3, column];
                jitteredProj[1, column] -= y * jitteredProj[3, column];
            }
            proj = GL.GetGPUProjectionMatrix(jitteredProj, true);
            projFlipY = GL.GetGPUProjectionMatrix(jitteredProj, false);
        }

        public static void CalculateProjectionMatrix(Camera camera, int2 renderSize, in float jitterSpread, int frameIndex, int jitterPhaseCount, ref float2 jitter, ref Matrix4x4 proj, ref Matrix4x4 projFlipY, bool applyJitter = true)
        {
            if (!applyJitter || jitterSpread <= 0.0f)
            {
                jitter = float2.zero;
                Matrix4x4 unjittered = camera.projectionMatrix;
                proj = GL.GetGPUProjectionMatrix(unjittered, true);
                projFlipY = GL.GetGPUProjectionMatrix(unjittered, false);
                return;
            }

            int phase = math.max(1, jitterPhaseCount);
            int sampleIndex = ((frameIndex % phase) + phase) % phase + 1;
            float jitterX = HaltonSequence.Get(sampleIndex, 2) - 0.5f;
            float jitterY = HaltonSequence.Get(sampleIndex, 3) - 0.5f;
            jitter = new float2(jitterX, jitterY) * jitterSpread;

            if (camera.orthographic)
            {
                GetJitteredOrthographicProjectionMatrix(camera, renderSize, jitter, ref proj, ref projFlipY);
            } 
            else
            {
                GetJitteredPerspectiveProjectionMatrix(camera, renderSize, jitter, ref proj, ref projFlipY);
            }
        }
    }
}
