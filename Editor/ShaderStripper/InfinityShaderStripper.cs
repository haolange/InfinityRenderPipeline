using System;
using System.Collections.Generic;
using InfinityTech.Rendering.Pipeline;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Editor.Build
{
    public sealed class InfinityBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!InfinityShaderStripper.IsInfinityActive) return;
            ValidateResources();
            InfinityShaderStripper.ResetCounts();
            InfinityComputeShaderStripper.ResetCounts();
        }

        public static void ValidateResources()
        {
            // This is also callable from Editor validation without starting a build.
            var resources = new InfinityRenderPipelineResources();
            ValidateContainer(resources.shaders);
            ValidateContainer(resources.textures);
            ValidateContainer(resources.materials);
        }

        static void ValidateContainer(IRenderPipelineResources resources)
        {
            foreach (var field in resources.GetType().GetFields())
            {
                if (!typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                if ((UnityEngine.Object)field.GetValue(resources) == null)
                    throw new BuildFailedException($"InfinityRP: missing resource {resources.GetType().Name}.{field.Name}. Assign the GlobalSettings resource before building.");
            }
        }
    }

    public sealed class InfinityBuildPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (!InfinityShaderStripper.IsInfinityActive) return;
            Debug.Log($"InfinityRP shader stripping: kept {InfinityShaderStripper.Kept}, stripped foreign-pipeline {InfinityShaderStripper.StrippedForeignTag}, " +
                $"stripped unused LightMode {InfinityShaderStripper.StrippedLightMode}; compute variants kept {InfinityComputeShaderStripper.Kept}.");
        }
    }

    public sealed class InfinityShaderStripper : IPreprocessShaders
    {
        static readonly ShaderTagId s_RenderPipelineTag = new ShaderTagId("RenderPipeline");
        static readonly ShaderTagId s_LightModeTag = new ShaderTagId("LightMode");
        // LightModes the pipeline actually draws. An empty LightMode is SRPDefaultUnlit. ShadowCaster is drawn by
        // the Unity shadow RendererList; Meta is consumed by the lightmapper.
        static readonly HashSet<string> s_UsedLightModes = new HashSet<string>
        {
            "", "SRPDefaultUnlit", "ShadowCaster", "Meta",
            InfinityPassIDs.DepthPass.name, InfinityPassIDs.DBufferPass.name, InfinityPassIDs.GBufferPass.name,
            InfinityPassIDs.ShadowPass.name, InfinityPassIDs.MotionPass.name, InfinityPassIDs.ForwardPass.name,
            InfinityPassIDs.TranslucentDepthPass.name, InfinityPassIDs.TranslucentT0Pass.name,
            InfinityPassIDs.TranslucentT1Pass.name, InfinityPassIDs.TranslucentT2Pass.name,
        };
        static int s_Kept;
        static int s_StrippedForeignTag;
        static int s_StrippedLightMode;
        static int s_StrippedProbeVolume;
        public int callbackOrder => 0;
        public static bool IsInfinityActive => GraphicsSettings.currentRenderPipeline is InfinityRenderPipelineAsset;
        public static int Kept => s_Kept;
        public static int StrippedForeignTag => s_StrippedForeignTag;
        public static int StrippedLightMode => s_StrippedLightMode;

        public static int StrippedProbeVolume => s_StrippedProbeVolume;

        public static void ResetCounts() { s_Kept = 0; s_StrippedForeignTag = 0; s_StrippedLightMode = 0; s_StrippedProbeVolume = 0; }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!IsInfinityActive || data == null || data.Count == 0) return;
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            // URP/HDRP package shaders carry their own RenderPipeline tag and are removed here.
            if (!HasCompatibleSubShader(shader))
            {
                s_StrippedForeignTag += data.Count;
                data.Clear();
                return;
            }

            // Inside an Infinity-tagged SubShader, a pass whose LightMode the pipeline never draws (for example a
            // Built-in ForwardBase/ForwardAdd pass) is dead weight. Untagged SubShaders (system blits, UI, terrain
            // details, material previews) keep every pass.
            if (IsUnusedInfinityPass(shader, snippet))
            {
                s_StrippedLightMode += data.Count;
                data.Clear();
                return;
            }

            StripUnusedProbeVolumeVariants(shader, data);
            s_Kept += data.Count;
        }

        // PROBE_VOLUMES_L1 / L2 variants exist for Adaptive Probe Volumes; a build keeps a band only when an Infinity
        // asset of the build (Graphics default or any Quality level) uses Adaptive Probe Volumes with that band.
        static void StripUnusedProbeVolumeVariants(Shader shader, IList<ShaderCompilerData> data)
        {
            UsedProbeVolumeBands(out bool l1, out bool l2);
            var keywordL1 = new ShaderKeyword(shader, "PROBE_VOLUMES_L1");
            var keywordL2 = new ShaderKeyword(shader, "PROBE_VOLUMES_L2");
            for (int i = data.Count - 1; i >= 0; --i)
            {
                ShaderKeywordSet keywords = data[i].shaderKeywordSet;
                if ((!l1 && keywords.IsEnabled(keywordL1)) || (!l2 && keywords.IsEnabled(keywordL2)))
                {
                    data.RemoveAt(i);
                    s_StrippedProbeVolume++;
                }
            }
        }

        internal static void UsedProbeVolumeBands(out bool l1, out bool l2)
        {
            l1 = false;
            l2 = false;
            var assets = new List<InfinityRenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline is InfinityRenderPipelineAsset defaultAsset) assets.Add(defaultAsset);
            for (int level = 0; level < QualitySettings.count; ++level)
            {
                if (QualitySettings.GetRenderPipelineAssetAt(level) is InfinityRenderPipelineAsset qualityAsset) assets.Add(qualityAsset);
            }
            foreach (InfinityRenderPipelineAsset asset in assets)
            {
                if (!asset.supportProbeVolume) continue;
                if (asset.probeVolumeSHBands == ProbeVolumeSHBands.SphericalHarmonicsL2) l2 = true;
                else l1 = true;
            }
        }

        internal static bool IsUnusedInfinityPass(Shader shader, in ShaderSnippetData snippet)
        {
            int subshader = (int)snippet.pass.SubshaderIndex;
            if (subshader < 0 || subshader >= shader.subshaderCount) return false;
            if (shader.FindSubshaderTagValue(subshader, s_RenderPipelineTag).name != "InfinityRenderPipeline") return false;
            string lightMode = shader.FindPassTagValue(subshader, (int)snippet.pass.PassIndex, s_LightModeTag).name;
            return !s_UsedLightModes.Contains(lightMode ?? string.Empty);
        }

        internal static bool HasCompatibleSubShader(Shader shader)
        {
            for (int index = 0; index < shader.subshaderCount; index++)
            {
                string tag = shader.FindSubshaderTagValue(index, s_RenderPipelineTag).name;
                if (string.IsNullOrEmpty(tag) || tag == "InfinityRenderPipeline") return true;
            }
            return false;
        }
    }

    public sealed class InfinityComputeShaderStripper : IPreprocessComputeShaders
    {
        static int s_Kept;
        public int callbackOrder => 0;
        public static int Kept => s_Kept;
        public static void ResetCounts() { s_Kept = 0; }
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (!InfinityShaderStripper.IsInfinityActive || data == null || data.Count == 0) return;
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            // Compute shaders have no RenderPipeline/LightMode contract. Preserve kernels
            // selected by Unity's dependency collection, including CoreRP UnifiedRayTracing.
            s_Kept += data.Count;
        }
    }
}
