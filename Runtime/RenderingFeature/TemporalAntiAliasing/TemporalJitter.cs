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

    internal static class TemporalJitter
    {
        public static void GetJitteredPerspectiveProjectionMatrix(Camera camera, float2 offset, ref Matrix4x4 proj, ref Matrix4x4 projFlipY)
        {
            float near = camera.nearClipPlane;
            float far = camera.farClipPlane;

            float vertical = Mathf.Tan(0.5f * Mathf.Deg2Rad * camera.fieldOfView) * near;
            float horizontal = vertical * camera.aspect;

            offset.x *= horizontal / (0.5f * camera.pixelWidth);
            offset.y *= vertical / (0.5f * camera.pixelHeight);

            proj = camera.projectionMatrix;

            proj[0, 2] += offset.x / horizontal;
            proj[1, 2] += offset.y / vertical;

            Matrix4x4 jitteredProj = proj;
            proj = GL.GetGPUProjectionMatrix(jitteredProj, true);
            projFlipY = GL.GetGPUProjectionMatrix(jitteredProj, false);
        }

        public static void GetJitteredOrthographicProjectionMatrix(Camera camera, float2 offset, ref Matrix4x4 proj, ref Matrix4x4 projFlipY)
        {
            float vertical = camera.orthographicSize;
            float horizontal = vertical * camera.aspect;

            offset.x *= horizontal / (0.5f * camera.pixelWidth);
            offset.y *= vertical / (0.5f * camera.pixelHeight);

            float left = offset.x - horizontal;
            float right = offset.x + horizontal;
            float top = offset.y + vertical;
            float bottom = offset.y - vertical;

            Matrix4x4 jitteredProj = Matrix4x4.Ortho(left, right, bottom, top, camera.nearClipPlane, camera.farClipPlane);
            proj = GL.GetGPUProjectionMatrix(jitteredProj, true);
            projFlipY = GL.GetGPUProjectionMatrix(jitteredProj, false);
        }

        public static void CalculateProjectionMatrix(Camera camera, in float jitterSpread, int frameIndex, ref float2 jitter, ref Matrix4x4 proj, ref Matrix4x4 projFlipY, bool applyJitter = true)
        {
            if (!applyJitter || jitterSpread <= 0.0f)
            {
                jitter = float2.zero;
                Matrix4x4 unjittered = camera.projectionMatrix;
                proj = GL.GetGPUProjectionMatrix(unjittered, true);
                projFlipY = GL.GetGPUProjectionMatrix(unjittered, false);
                return;
            }

            float jitterX = HaltonSequence.Get((frameIndex & 1023) + 1, 2) - 0.5f;
            float jitterY = HaltonSequence.Get((frameIndex & 1023) + 1, 3) - 0.5f;
            jitter = new float2(jitterX, jitterY);
            jitter *= jitterSpread;

            if (camera.orthographic)
            {
                GetJitteredOrthographicProjectionMatrix(camera, jitter, ref proj, ref projFlipY);
            } 
            else
            {
                GetJitteredPerspectiveProjectionMatrix(camera, jitter, ref proj, ref projFlipY);
            }
        }
    }
}
