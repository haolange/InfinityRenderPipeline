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

    public sealed class InfinityShaderStripper : IPreprocessShaders
    {
        static readonly ShaderTagId s_RenderPipelineTag = new ShaderTagId("RenderPipeline");
        static int s_Kept;
        static int s_StrippedForeignTag;
        public int callbackOrder => 0;
        public static bool IsInfinityActive => GraphicsSettings.currentRenderPipeline is InfinityRenderPipelineAsset;
        public static int Kept => s_Kept;
        public static int StrippedForeignTag => s_StrippedForeignTag;

        public static void ResetCounts() { s_Kept = 0; s_StrippedForeignTag = 0; }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!IsInfinityActive || data == null || data.Count == 0) return;
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            // passName is not LightMode. Preserve all passes in a compatible shader, including
            // ShadowCaster, SRPDefaultUnlit, Terrain, Meta, material previews and system blits.
            if (HasCompatibleSubShader(shader))
                s_Kept += data.Count;
            else
            {
                s_StrippedForeignTag += data.Count;
                data.Clear();
            }
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
