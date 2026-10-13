using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    /// <summary>
    /// Material upgraders to InfinityLit / InfinityUnlit, registered with CoreRP's MaterialUpgrader registry (so
    /// Edit/Rendering/Materials/Convert ... to Current SRP uses them) and listed by the Material Conversion Wizard.
    /// InfinityLit is metallic / roughness with one base color map, one normal map and an emission color: smoothness
    /// becomes 1 - roughness, the base map tiling x becomes _BaseColorTile, masks (metallic / occlusion / detail /
    /// height maps) and emission maps have no InfinityLit input and are dropped (the wizard reports them).
    /// </summary>
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    sealed class InfinityMaterialUpgradersProvider : IMaterialUpgradersProvider
    {
        public IEnumerable<MaterialUpgrader> GetUpgraders() => InfinityMaterialUpgraders.All;
    }

    static class InfinityMaterialUpgraders
    {
        public const string LitShader = "InfinityPipeline/InfinityLit";
        public const string UnlitShader = "InfinityPipeline/InfinityUnlit";

        static List<MaterialUpgrader> s_All;

        public static List<MaterialUpgrader> All => s_All ??= Create();

        static List<MaterialUpgrader> Create()
        {
            return new List<MaterialUpgrader>
            {
                new InfinityLitUpgrader(InfinityLitUpgrader.ESource.BuiltinStandard, "Standard"),
                new InfinityLitUpgrader(InfinityLitUpgrader.ESource.BuiltinSpecular, "Standard (Specular setup)"),
                new InfinityLitUpgrader(InfinityLitUpgrader.ESource.BuiltinLegacy, "Legacy Shaders/Diffuse"),
                new InfinityLitUpgrader(InfinityLitUpgrader.ESource.BuiltinLegacy, "Legacy Shaders/Bumped Diffuse"),
                new InfinityLitUpgrader(InfinityLitUpgrader.ESource.BuiltinLegacy, "Legacy Shaders/Specular"),
                new InfinityLitUpgrader(InfinityLitUpgrader.ESource.BuiltinLegacy, "Legacy Shaders/Bumped Specular"),
                new InfinityLitUpgrader(InfinityLitUpgrader.ESource.UniversalLit, "Universal Render Pipeline/Lit"),
                new InfinityLitUpgrader(InfinityLitUpgrader.ESource.UniversalLit, "Universal Render Pipeline/Complex Lit"),
                new InfinityLitUpgrader(InfinityLitUpgrader.ESource.UniversalSimpleLit, "Universal Render Pipeline/Simple Lit"),
                new InfinityLitUpgrader(InfinityLitUpgrader.ESource.HighDefinitionLit, "HDRP/Lit"),
                new InfinityLitUpgrader(InfinityLitUpgrader.ESource.HighDefinitionLit, "HDRP/LitTessellation"),
                new InfinityUnlitUpgrader("Unlit/Texture", "_MainTex", "_Color"),
                new InfinityUnlitUpgrader("Unlit/Color", "_MainTex", "_Color"),
                new InfinityUnlitUpgrader("Unlit/Transparent", "_MainTex", "_Color"),
                new InfinityUnlitUpgrader("Unlit/Transparent Cutout", "_MainTex", "_Color"),
                new InfinityUnlitUpgrader("Universal Render Pipeline/Unlit", "_BaseMap", "_BaseColor"),
                new InfinityUnlitUpgrader("HDRP/Unlit", "_UnlitColorMap", "_UnlitColor"),
            };
        }

        public static MaterialUpgrader Find(Material material)
        {
            if (material == null || material.shader == null) return null;
            string name = material.shader.name;
            foreach (MaterialUpgrader upgrader in All)
            {
                if (upgrader.OldShaderPath == name) return upgrader;
            }
            return null;
        }

        // Inputs of the source material InfinityLit cannot represent (reported by the wizard, not converted).
        public static List<string> DroppedInputs(Material material)
        {
            var dropped = new List<string>();
            string[] maps =
            {
                "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap", "_ParallaxMap", "_DetailAlbedoMap", "_DetailNormalMap",
                "_DetailMask", "_EmissionMap", "_MaskMap", "_EmissiveColorMap", "_HeightMap", "_DetailMap", "_CoatMask"
            };
            foreach (string map in maps)
            {
                if (material.HasProperty(map) && material.GetTexture(map) != null) dropped.Add(map);
            }
            if (material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f) dropped.Add("alpha clipping");
            if (material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > 0.5f) dropped.Add("alpha clipping");
            if (material.IsKeywordEnabled("_ALPHATEST_ON")) dropped.Add("alpha clipping");
            return dropped;
        }

        // Transparent sources draw as translucent stage T0 (lit, before the refraction pyramid); opaque ones stay deferred.
        internal static bool IsTransparent(Material material)
        {
            if (material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f) return true; // URP
            if (material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f) return true; // HDRP
            if (material.HasProperty("_Mode") && material.GetFloat("_Mode") >= 2.0f) return true; // Built-in Fade / Transparent
            return material.renderQueue >= (int)RenderQueue.Transparent;
        }
    }

    sealed class InfinityLitUpgrader : MaterialUpgrader
    {
        public enum ESource
        {
            BuiltinStandard,
            BuiltinSpecular,
            BuiltinLegacy,
            UniversalLit,
            UniversalSimpleLit,
            HighDefinitionLit,
        }

        readonly ESource m_Source;
        readonly string m_BaseMap;

        public InfinityLitUpgrader(ESource source, string oldShader)
        {
            m_Source = source;
            RenameShader(oldShader, InfinityMaterialUpgraders.LitShader, Finalize);
            switch (source)
            {
                case ESource.BuiltinStandard:
                case ESource.BuiltinSpecular:
                case ESource.BuiltinLegacy:
                    m_BaseMap = "_MainTex";
                    RenameColor("_Color", "_BaseColor");
                    RenameTexture("_BumpMap", "_NormalTexture");
                    RenameFloat("_Metallic", "_Reflectance");
                    break;
                case ESource.UniversalLit:
                case ESource.UniversalSimpleLit:
                    m_BaseMap = "_BaseMap";
                    RenameTexture("_BaseMap", "_MainTex");
                    RenameTexture("_BumpMap", "_NormalTexture");
                    RenameFloat("_Metallic", "_Reflectance");
                    break;
                case ESource.HighDefinitionLit:
                    m_BaseMap = "_BaseColorMap";
                    RenameTexture("_BaseColorMap", "_MainTex");
                    RenameTexture("_NormalMap", "_NormalTexture");
                    RenameFloat("_Metallic", "_Reflectance");
                    RenameColor("_EmissiveColor", "_EmissionColor");
                    break;
            }
        }

        public override void Convert(Material source, Material destination)
        {
            base.Convert(source, destination);
            destination.SetFloat("_Roughness", 1.0f - Smoothness(source));
            if (m_Source == ESource.BuiltinSpecular || m_Source == ESource.BuiltinLegacy || m_Source == ESource.UniversalSimpleLit)
            {
                // Specular color workflow: dielectric, with the specular level from the F0 luminance (0.5 = 4%).
                destination.SetFloat("_Reflectance", 0.0f);
                Color specular = source.HasProperty("_SpecColor") ? source.GetColor("_SpecColor") : new Color(0.2f, 0.2f, 0.2f);
                destination.SetFloat("_SpecularLevel", Mathf.Clamp01(specular.grayscale / 0.08f));
            }
            else
            {
                destination.SetFloat("_SpecularLevel", 0.5f);
            }

            if (source.HasProperty(m_BaseMap))
            {
                destination.SetFloat("_BaseColorTile", Mathf.Max(0.0f, source.GetTextureScale(m_BaseMap).x));
            }

            bool emissive = m_Source == ESource.HighDefinitionLit || source.IsKeywordEnabled("_EMISSION");
            Color emission = m_Source == ESource.HighDefinitionLit
                ? (source.HasProperty("_EmissiveColor") ? source.GetColor("_EmissiveColor") : Color.black)
                : (source.HasProperty("_EmissionColor") ? source.GetColor("_EmissionColor") : Color.black);
            destination.SetColor("_EmissionColor", emissive ? emission : Color.black);

            destination.SetFloat("_SurfaceRoute", 0.0f);
            destination.SetFloat("_TranslucentStage", InfinityMaterialUpgraders.IsTransparent(source) ? 1.0f : 0.0f);
        }

        float Smoothness(Material source)
        {
            switch (m_Source)
            {
                case ESource.BuiltinStandard:
                case ESource.BuiltinSpecular:
                    return source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : 0.5f;
                case ESource.BuiltinLegacy:
                    return source.HasProperty("_Shininess") ? Mathf.Clamp01(source.GetFloat("_Shininess")) : 0.3f;
                default:
                    return source.HasProperty("_Smoothness") ? source.GetFloat("_Smoothness") : 0.5f;
            }
        }

        // Runs on the upgraded material: the base color map toggle and the route pass state.
        static void Finalize(Material material)
        {
            bool hasMap = material.GetTexture("_MainTex") != null;
            material.SetFloat("UseBaseColorTex", hasMap ? 1.0f : 0.0f);
            if (hasMap) material.EnableKeyword("_UseAlbedoTex");
            else material.DisableKeyword("_UseAlbedoTex");
            material.renderQueue = -1;
            MaterialRouteUtility.ApplyPassState(material);
        }
    }

    sealed class InfinityUnlitUpgrader : MaterialUpgrader
    {
        public InfinityUnlitUpgrader(string oldShader, string baseMap, string baseColor)
        {
            RenameShader(oldShader, InfinityMaterialUpgraders.UnlitShader);
            if (baseMap != "_MainTex") RenameTexture(baseMap, "_MainTex");
            if (baseColor != "_BaseColor") RenameColor(baseColor, "_BaseColor");
        }
    }
}
