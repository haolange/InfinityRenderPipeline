using System.Collections.Generic;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    // The only runtime-probe code that uses the MeshComponent authoring API and the mesh scene. It changes with that API;
    // RuntimeProbeCommands.cs (the measurement) does not.
    internal static class RuntimeProbeContent
    {
        const string k_Shader = "InfinityPipeline/InfinityLit-Instanced";
        static readonly int ID_SurfaceRoute = Shader.PropertyToID("_SurfaceRoute");
        static readonly int ID_TranslucentStage = Shader.PropertyToID("_TranslucentStage");

        internal static void Spawn(RuntimeProbeRequest request, Camera camera, List<Object> owned, List<Transform> moving, List<Vector3> basePositions, out int translucent)
        {
            translucent = 0;
            if (request.count == 0) return;
            Shader shader = Shader.Find(k_Shader);
            if (shader == null) throw new System.InvalidOperationException(k_Shader + " is not loaded.");
            Material opaque = CreateMaterial(shader, 0, owned);
            Material translucentMaterial = CreateMaterial(shader, 1, owned);

            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            // Destroy now so the primitive's native renderer never reaches a frame.
            Object.DestroyImmediate(primitive);

            int side = Mathf.CeilToInt(Mathf.Sqrt(request.count));
            int movingCount = Mathf.RoundToInt(request.count * request.movingRatio);
            int translucentCount = Mathf.RoundToInt(request.count * request.translucentRatio);
            Transform view = camera.transform;
            Vector3 origin = view.position + view.forward * 4.0f - view.up * 1.5f - view.right * (side * request.spacing * 0.5f);
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int i = 0; i < request.count; ++i)
            {
                Vector3 position = origin + right * ((i % side) * request.spacing) + forward * ((i / side) * request.spacing);
                // Every object is a root so the transform job is not serialized behind one hierarchy.
                var go = new GameObject("InfinityRuntimeProbe") { hideFlags = HideFlags.DontSave };
                go.transform.SetPositionAndRotation(position, Quaternion.identity);
                go.transform.localScale = Vector3.one * 0.5f;
                bool isMoving = i < movingCount;
                bool isTranslucent = i >= request.count - translucentCount;
                MeshComponent component = go.AddComponent<MeshComponent>();
                component.Mobility = isMoving ? EStateType.Dynamic : EStateType.Static;
                component.MeshAsset = mesh;
                component.SetMaterials(new[] { isTranslucent ? translucentMaterial : opaque });
                component.CastShadow = request.castShadow && !isTranslucent ? ECastShadowMethod.Dynamic : ECastShadowMethod.Off;
                owned.Add(go);
                if (isMoving) { moving.Add(go.transform); basePositions.Add(position); }
                if (isTranslucent) ++translucent;
            }
        }

        // Opaque uses the Deferred route; translucent stages use the Forward route, as the visual fixtures do.
        static Material CreateMaterial(Shader shader, int translucentStage, List<Object> owned)
        {
            var material = new Material(shader) { hideFlags = HideFlags.DontSave, name = translucentStage == 0 ? "InfinityRuntimeProbeOpaque" : "InfinityRuntimeProbeTranslucent" };
            material.SetFloat(ID_SurfaceRoute, translucentStage == 0 ? 0.0f : 1.0f);
            material.SetFloat(ID_TranslucentStage, translucentStage);
            MaterialRouteUtility.ApplyPassState(material);
            owned.Add(material);
            return material;
        }

        internal static void ReadMeshState(InfinityRenderPipeline pipeline, out int entities, out int elements)
        {
            MeshScene scene = pipeline.renderContext.GetMeshScene();
            entities = scene.LogicalInstanceCount;
            elements = scene.DrawCount;
        }
    }
}
