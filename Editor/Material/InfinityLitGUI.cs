using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.MaterialPipeline;

namespace InfinityTech.Rendering.Editor
{
    public class InfinityLitGUI : ShaderGUI
    {
        const string FoldoutPrefix = "InfinityRP.LitGUI.Foldout.";

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material material = materialEditor.target as Material;
            float previousRoute = ReadFloat(material, "_SurfaceRoute");
            float previousStage = ReadFloat(material, "_TranslucentStage");
            EditorGUI.BeginChangeCheck();

            DrawGroup(materialEditor, properties, "Surface Options", true, "_SurfaceRoute", "_TranslucentStage", "_RefractionStrength");
            DrawGroup(materialEditor, properties, "Surface Inputs", true,
                "UseBaseColorTex", "_MainTex", "_BaseColor", "_BaseColorTile", "_EmissionColor",
                "_Roughness", "_Reflectance", "_SpecularLevel",
                "_NormalTexture", "_NormalTile",
                "Iridescence", "_Iridescence_Distance",
                "_PixelDepthOffset");

            // Keep GI explicit: graph emission cannot be inferred from a fixed _EmissionColor property.
            EditorGUILayout.LabelField("Baked Emission", EditorStyles.boldLabel);
            materialEditor.LightmapEmissionFlagsProperty(0, true, true);

            DrawGroup(materialEditor, properties, "Advanced / Render State", false, "_ZTest", "_ZWrite");

            if (EditorGUI.EndChangeCheck())
            {
                bool routeChanged = !Mathf.Approximately(ReadFloat(material, "_SurfaceRoute"), previousRoute) ||
                    !Mathf.Approximately(ReadFloat(material, "_TranslucentStage"), previousStage);
                ApplyTargets(materialEditor, routeChanged);
            }
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

        internal static void DrawGroup(MaterialEditor materialEditor, MaterialProperty[] properties, string title, bool defaultOpen, params string[] names)
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

        static float ReadFloat(Material material, string name)
        {
            return material != null && material.HasProperty(name) ? material.GetFloat(name) : float.NaN;
        }

        internal static void ApplyTargets(MaterialEditor materialEditor, bool routeChanged)
        {
            if (!routeChanged)
            {
                return;
            }

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

        internal static void DirtyMeshComponents(Material material)
        {
            MeshComponent.NotifyMaterialRoutingChanged(material);
        }
    }
}
