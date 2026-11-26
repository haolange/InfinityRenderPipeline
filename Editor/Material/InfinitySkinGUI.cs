using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.MaterialPipeline;

namespace InfinityTech.Rendering.Editor
{
    // InfinitySkin is always Forward opaque; its routing values are hidden and only its surface, specular and profile show.
    public sealed class InfinitySkinGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material material = materialEditor.target as Material;
            InfinityLitGUI.DrawGroup(materialEditor, properties, "Surface Inputs", true,
                "_MainTex", "_BaseColor", "_BaseColorTile",
                "_NormalTexture", "_NormalTile",
                "_OcclusionTexture", "_OcclusionStrength");
            InfinityLitGUI.DrawGroup(materialEditor, properties, "Specular", true,
                "_Roughness", "_SpecularLevel", "_SecondLobeMix", "_SecondLobeRoughnessScale");
            InfinityLitGUI.DrawGroup(materialEditor, properties, "Subsurface", true, "_DiffusionProfileIndex");
            DrawProfileWarning(material);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            MaterialRouteUtility.ApplyPassState(material);
        }

        public override void ValidateMaterial(Material material)
        {
            MaterialRouteUtility.Read(material, out _, out _);
        }

        // The profile index selects a slot of the active pipeline asset's Diffusion Profile list.
        static void DrawProfileWarning(Material material)
        {
            InfinityRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset;
            DiffusionProfile[] profiles = asset != null ? asset.ReadDiffusionProfiles() : null;
            int index = (int)material.GetFloat("_DiffusionProfileIndex");
            if (profiles == null || index >= profiles.Length)
            {
                EditorGUILayout.HelpBox("Diffusion profile " + index + " is outside the pipeline asset's Diffusion Profile list; this skin is not scattered.", MessageType.Warning);
                return;
            }
            if (profiles[index] == null)
            {
                EditorGUILayout.HelpBox("Diffusion profile slot " + index + " is empty; this skin is not scattered.", MessageType.Warning);
            }
        }
    }
}
