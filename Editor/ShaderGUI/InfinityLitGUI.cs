using UnityEditor;
using UnityEngine;
using InfinityTech.Component;

namespace InfinityTech.Rendering.Editor
{
    public class InfinityLitGUI : ShaderGUI
    {
        const string PassGBuffer = "GBufferPass";
        const string PassForward = "ForwardPass";
        const string PassDepth = "DepthPass";
        const string PassShadow = "ShadowPass";
        const string PassShadowCaster = "ShadowCaster";
        const string PassMotion = "MotionPass";
        const string PassTranslucentDepth = "TranslucentDepthPass";
        const string PassTranslucentT0 = "TranslucentT0Pass";
        const string PassTranslucentT1 = "TranslucentT1Pass";
        const string PassTranslucentT2 = "TranslucentT2Pass";
        const string FoldoutPrefix = "InfinityRP.LitGUI.Foldout.";

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            DrawGroup(materialEditor, properties, "Color", true, "_UseAlbedoTex", "_MainTex", "_BaseColor", "_BaseColorTile", "_EmissionColor");
            DrawGroup(materialEditor, properties, "Microface", true, "_Roughness", "_Reflectance", "_SpecularLevel");
            DrawGroup(materialEditor, properties, "Normal", true, "_NomralTexture", "_NormalTile");
            DrawGroup(materialEditor, properties, "Iridescence", false, "_Iridescence", "_Iridescence_Distance");
            DrawGroup(materialEditor, properties, "PixelDepthOffset", false, "_PixelDepthOffsetVaule");
            DrawGroup(materialEditor, properties, "Subsurface", false, "_Subsurface", "_SSSProfileIndex", "_SSSThickness");
            DrawGroup(materialEditor, properties, "SurfaceRoute", true, "_SurfaceRoute", "_TranslucentStage", "_RefractionStrength");
            DrawGroup(materialEditor, properties, "RenderState", false, "_ZTest", "_ZWrite");
            ApplyTargets(materialEditor);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            ApplyPassState(material);
        }

        public override void ValidateMaterial(Material material)
        {
            ApplyPassState(material);
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
                    ApplyPassState(material);
                    DirtyMeshComponents(material);
                }
            }
        }

        public static void ApplyPassState(Material material)
        {
            if (material == null)
            {
                return;
            }

            int route = material.HasProperty("_SurfaceRoute") ? Mathf.RoundToInt(material.GetFloat("_SurfaceRoute")) : 0;
            int stage = material.HasProperty("_TranslucentStage") ? Mathf.RoundToInt(material.GetFloat("_TranslucentStage")) : 0;
            bool translucent = stage > 0;
            bool deferred = !translucent && route == 0;
            bool forward = !translucent && route == 1;

            SetPass(material, PassGBuffer, deferred);
            SetPass(material, PassForward, forward);
            SetPass(material, PassDepth, !translucent);
            SetPass(material, PassShadow, !translucent);
            SetPass(material, PassShadowCaster, !translucent);
            SetPass(material, PassMotion, !translucent);
            SetPass(material, PassTranslucentDepth, translucent);
            SetPass(material, PassTranslucentT0, translucent && stage == 1);
            SetPass(material, PassTranslucentT1, translucent && stage == 2);
            SetPass(material, PassTranslucentT2, translucent && stage == 3);

            if (translucent)
            {
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                material.SetOverrideTag("RenderType", "Opaque");
                if (material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
                {
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                }
            }
        }

        static void SetPass(Material material, string passName, bool enabled)
        {
            if (material.FindPass(passName) >= 0)
            {
                material.SetShaderPassEnabled(passName, enabled);
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
