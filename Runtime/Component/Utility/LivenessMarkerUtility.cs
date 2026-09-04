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
            go.transform.localScale = Vector3.one * 0.25f;
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

            // Bottom-right of the view, ~0.35m in front so Game-view captures show motion.
            target.position = camera.transform.position
                + camera.transform.forward * 0.35f
                + camera.transform.right * 0.18f
                + camera.transform.up * -0.12f;
        }
    }
}
