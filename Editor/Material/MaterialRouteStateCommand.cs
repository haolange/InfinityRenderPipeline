using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.MaterialPipeline;

namespace InfinityTech.Rendering.Editor
{
    // Explicit command that applies the derived route state (pass enables, GBuffer stencil reference) to every routed material
    // under Assets, with Undo and without saving. Materials authored before a derived property existed keep its shader default
    // until their route is applied.
    internal static class MaterialRouteStateCommand
    {
        [MenuItem("Window/Infinity/Materials/Apply Surface Route State To Project Materials")]
        static void ApplyToProjectMaterials()
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply Surface Route State");
            int applied = 0;
            int rejected = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || !material.HasProperty("_SurfaceRoute"))
                {
                    continue;
                }
                bool emissiveBefore = material.GetShaderPassEnabled(MaterialRouteUtility.EmissivePassName);
                Undo.RecordObject(material, "Apply Surface Route State");
                try
                {
                    MaterialRouteUtility.ApplyPassState(material);
                    applied++;
                    if (material.GetShaderPassEnabled(MaterialRouteUtility.EmissivePassName) != emissiveBefore)
                    {
                        AssetDatabase.SaveAssetIfDirty(material);
                    }
                }
                catch (System.InvalidOperationException error)
                {
                    // A material with an invalid route is reported with its context and left for an explicit correction.
                    Debug.LogError("[InfinityRP] " + error.Message, material);
                    rejected++;
                }
            }
            Undo.CollapseUndoOperations(group);
            Debug.Log("[InfinityRP] Applied the surface route state to " + applied + " materials; " + rejected + " rejected. Save the project to keep the change.");
        }
    }
}
