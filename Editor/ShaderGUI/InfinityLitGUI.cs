using UnityEditor;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    public class InfinityLitGUI : ShaderGUI
    {
        const string FoldoutPrefix = "InfinityRP.LitGUI.Foldout.";

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            EditorGUI.BeginChangeCheck();
            DrawGroup(materialEditor, properties, "Color", true, "_UseAlbedoTex", "_MainTex", "_BaseColor", "_BaseColorTile", "_EmissionColor");
            DrawGroup(materialEditor, properties, "Microface", true, "_Roughness", "_Reflectance", "_SpecularLevel");
            DrawGroup(materialEditor, properties, "Normal", true, "_NomralTexture", "_NormalTile");
            DrawGroup(materialEditor, properties, "Iridescence", false, "_Iridescence", "_Iridescence_Distance");
            DrawGroup(materialEditor, properties, "PixelDepthOffset", false, "_PixelDepthOffsetVaule");
            DrawGroup(materialEditor, properties, "Subsurface", false, "_Subsurface", "_SSSProfileIndex", "_SSSThickness");
            DrawGroup(materialEditor, properties, "SurfaceRoute", true, "_SurfaceRoute", "_TranslucentStage", "_RefractionStrength");
            DrawGroup(materialEditor, properties, "RenderState", false, "_ZTest", "_ZWrite");
            if (EditorGUI.EndChangeCheck())
                ApplyTargets(materialEditor);
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

        static void DrawGroup(MaterialEditor materialEditor, MaterialProperty[] properties, string title, bool defaultOpen, params string[] names)
        {
            string key = FoldoutPrefix + title;
            bool open = SessionState.GetBool(key, defaultOpen);
            bool next = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
            if (next != open)
            {
                SessionState.SetBool(key, next);
            }

            if (next)
            {
                for (int i = 0; i < names.Length; ++i)
                {
                    MaterialProperty property = FindProperty(names[i], properties, false);
                    if (property != null)
                    {
                        materialEditor.ShaderProperty(property, property.displayName);
                    }
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        static void ApplyTargets(MaterialEditor materialEditor)
        {
            Object[] targets = materialEditor.targets;
            for (int i = 0; i < targets.Length; ++i)
            {
                if (targets[i] is Material material)
                {
                    MaterialRouteUtility.ApplyPassState(material);
                    DirtyMeshComponents(material);
                }
            }
        }

        static void DirtyMeshComponents(Material material)
        {
            MeshComponent[] meshes = Object.FindObjectsByType<MeshComponent>();
            for (int i = 0; i < meshes.Length; ++i)
            {
                MeshComponent mesh = meshes[i];
                if (mesh == null || mesh.materials == null)
                {
                    continue;
                }

                for (int m = 0; m < mesh.materials.Length; ++m)
                {
                    if (mesh.materials[m] == material)
                    {
                        mesh.MarkDirty();
                        break;
                    }
                }
            }
        }
    }
}
