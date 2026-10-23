using System;
using UnityEngine;

namespace InfinityTech.Component
{
    public static class LivenessMarkerUtility
    {
        public const string ObjectName = "LivenessMarker";
        public const string LitShaderName = "InfinityPipeline/InfinityLit";

        public static LivenessMarker EnsureInScene(Camera camera)
        {
            GameObject existing = GameObject.Find(ObjectName);
            if (existing != null)
            {
                LivenessMarker marker = existing.GetComponent<LivenessMarker>();
                if (marker == null)
                {
                    marker = existing.AddComponent<LivenessMarker>();
                }

                return marker;
            }

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = ObjectName;
            PlaceInCameraNearCorner(go.transform, camera);
            return go.AddComponent<LivenessMarker>();
        }

        public static Material ApplyLitMaterial(GameObject go, Color albedo)
        {
            if (go == null)
            {
                throw new ArgumentNullException(nameof(go));
            }

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                throw new InvalidOperationException("InfinityRP: LivenessMarker is missing a MeshRenderer.");
            }

            Material current = renderer.sharedMaterial;
            if (current != null && current.shader != null && current.shader.name == LitShaderName)
            {
                return current;
            }

            Shader shader = Shader.Find(LitShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException("InfinityRP: InfinityPipeline/InfinityLit shader missing.");
            }

            Material material = new Material(shader);
            material.SetColor("_BaseColor", albedo);
            renderer.sharedMaterial = material;
            return material;
        }

        public static void PlaceInCameraNearCorner(Transform target, Camera camera)
        {
            if (target == null || camera == null)
            {
                return;
            }

            // Size the fixture cube against the shorter viewport dimension. Its rotating bounding sphere must stay
            // inside both the corner region and the clip range; a fixed distance can put its faces through near.
            const float halfDiagonalPerSide = 0.8660254f;
            float clipSpan = camera.farClipPlane - camera.nearClipPlane;
            float padding = Mathf.Min(0.05f, clipSpan * 0.01f);
            float near = camera.nearClipPlane + padding;
            float far = camera.farClipPlane - padding;
            float shortAspect = Mathf.Min(1f, camera.aspect);
            float radius;
            float distance;
            if (camera.orthographic)
            {
                radius = camera.orthographicSize * shortAspect * 0.12f * halfDiagonalPerSide;
                radius = Mathf.Min(radius, (far - near) * 0.45f);
                distance = Mathf.Clamp(1f, near + radius, far - radius);
            }
            else
            {
                float shortTangent = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * shortAspect;
                float radiusPerDepth = shortTangent * 0.12f * halfDiagonalPerSide;
                // Distance from the projected center (.9, .1) to the outer viewport planes, with a 20% margin.
                float cornerRadiusPerDepth = 0.16f * shortTangent / Mathf.Sqrt(1f + shortTangent * shortTangent);
                radiusPerDepth = Mathf.Min(radiusPerDepth, cornerRadiusPerDepth,
                    0.9f * (far - near) / (far + near));
                distance = Mathf.Clamp(1f, near / (1f - radiusPerDepth), far / (1f + radiusPerDepth));
                radius = radiusPerDepth * distance;
            }
            target.localScale = Vector3.one * (radius / halfDiagonalPerSide);
            target.position = camera.ViewportToWorldPoint(new Vector3(0.9f, 0.1f, distance));
        }
    }
}
