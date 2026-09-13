using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Pipeline;

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
                "_UseAlbedoTex", "_MainTex", "_BaseColor", "_BaseColorTile", "_EmissionColor",
                "_Roughness", "_Reflectance", "_SpecularLevel",
                "_NormalTexture", "_NomralTexture", "_NormalTile",
                "_Iridescence", "_Iridescence_Distance",
                "_PixelDepthOffset", "_PixelDepthOffsetVaule");

            bool subsurface = material != null && material.HasProperty("_Subsurface") && material.GetFloat("_Subsurface") > 0.5f;
            if (subsurface || SessionState.GetBool(FoldoutPrefix + "Subsurface", false))
            {
                DrawGroup(materialEditor, properties, "Subsurface", false, "_Subsurface", "_SSSProfileIndex", "_SSSThickness");
                DrawSssIndexWarning(material);
            }

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
            InfinityMaterialMigration.Migrate(material);
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

        static void DrawSssIndexWarning(Material material)
        {
            if (material == null || !material.HasProperty("_SSSProfileIndex"))
            {
                return;
            }

            InfinityRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset;
            int index = (int)material.GetFloat("_SSSProfileIndex");
            DiffusionProfile[] profiles = asset != null ? asset.diffusionProfiles : null;
            if (profiles == null || index < 0 || index >= profiles.Length)
            {
                EditorGUILayout.HelpBox("SSS profile index is outside the RP Asset Diffusion Profile list.", MessageType.Warning);
                return;
            }

            if (profiles[index] == null)
            {
                EditorGUILayout.HelpBox("SSS profile slot " + index + " is empty.", MessageType.Warning);
            }
        }

        static float ReadFloat(Material material, string name)
        {
            return material != null && material.HasProperty(name) ? material.GetFloat(name) : float.NaN;
        }

        static void ApplyTargets(MaterialEditor materialEditor, bool routeChanged)
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
