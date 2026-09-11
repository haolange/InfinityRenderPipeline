using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public enum ERayTracingBackend
    {
        Unavailable = 0,
        Hardware = 1,
        Compute = 2
    }

    public static class InfinityRayTracingEnvironment
    {
        const string ContextTypeName = "UnityEngine.Rendering.UnifiedRayTracing.RayTracingContext, Unity.RenderPipelines.Core.Runtime";

        public static ERayTracingBackend lastBackend { get; private set; } = ERayTracingBackend.Unavailable;

        public static ERayTracingBackend ResolveBackend()
        {
            Type contextType = Type.GetType(ContextTypeName);
            if (contextType != null)
            {
                MethodInfo supported = contextType.GetMethod("IsBackendSupported", BindingFlags.Public | BindingFlags.Static);
                if (supported != null)
                {
                    if (TryBackend(supported, 0))
                    {
                        lastBackend = ERayTracingBackend.Hardware;
                        return lastBackend;
                    }

                    if (TryBackend(supported, 1))
                    {
                        lastBackend = ERayTracingBackend.Compute;
                        return lastBackend;
                    }
                }

                lastBackend = ERayTracingBackend.Compute;
                return lastBackend;
            }

            lastBackend = SystemInfo.supportsRayTracing ? ERayTracingBackend.Hardware : ERayTracingBackend.Unavailable;
            return lastBackend;
        }

        public static bool CanRecord(InfinityRenderPipelineAsset asset, InfinityRenderPipelineRuntimeShaders shaders)
        {
            if (asset == null || !asset.enableRayTrace)
            {
                lastBackend = ERayTracingBackend.Unavailable;
                return false;
            }

            if (!GraphicsUtility.HasRequiredKernels(shaders != null ? shaders.rtaoShader : null, "RTAOTrace"))
            {
                lastBackend = ERayTracingBackend.Unavailable;
                return false;
            }

            return ResolveBackend() != ERayTracingBackend.Unavailable;
        }

        static bool TryBackend(MethodInfo supported, int backend)
        {
            try
            {
                object boxed = Enum.ToObject(supported.GetParameters()[0].ParameterType, backend);
                return (bool)supported.Invoke(null, new[] { boxed });
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
