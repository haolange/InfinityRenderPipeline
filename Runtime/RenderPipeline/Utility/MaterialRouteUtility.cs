using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public static class MaterialRouteUtility
    {
        static readonly int ID_SurfaceRoute = Shader.PropertyToID("_SurfaceRoute");
        static readonly int ID_TranslucentStage = Shader.PropertyToID("_TranslucentStage");
        static readonly int ID_Subsurface = Shader.PropertyToID("_Subsurface");

        public static void Read(Material material, out int surfaceRoute, out int translucentStage)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));

            surfaceRoute = ReadIndex(material, ID_SurfaceRoute, "_SurfaceRoute", 1);
            translucentStage = ReadIndex(material, ID_TranslucentStage, "_TranslucentStage", 3);
            if (material.HasProperty(ID_Subsurface) && material.GetFloat(ID_Subsurface) > 0.5f
                && (surfaceRoute != 0 || translucentStage != 0))
                throw new InvalidOperationException($"Material '{material.name}' requires Deferred opaque routing for subsurface scattering.");
        }

        static int ReadIndex(Material material, int property, string name, int maximum)
        {
            if (!material.HasProperty(property))
                throw new InvalidOperationException($"Material '{material.name}' shader must declare {name} for Infinity surface routing.");

            float value = material.GetFloat(property);
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > maximum || value != Mathf.Floor(value))
                throw new InvalidOperationException($"Material '{material.name}' {name} must be an integer in [0, {maximum}].");
            return (int)value;
        }

        // Explicit material update only. Import and validation must not apply serialized state.
        public static void ApplyPassState(Material material)
        {
            Read(material, out int route, out int stage);
            bool opaque = stage == 0;
            // Both opaque routes provide surface data for AO, ray tracing and motion consumers.
            // Forward pixels are tagged so Deferred never shades them.
            SetPass(material, "RasterGBufferPass", opaque);
            SetPass(material, "RasterForwardPass", opaque && route == 1);
            SetPass(material, "RasterDepthPass", opaque);
            SetPass(material, "RasterShadowPass", opaque);
            SetPass(material, "ShadowCaster", opaque);
            SetPass(material, "RasterMotionPass", opaque);
            SetPass(material, "RasterTranslucentDepthPass", !opaque);
            SetPass(material, "RasterTranslucentT0Pass", stage == 1);
            SetPass(material, "RasterTranslucentT1Pass", stage == 2);
            SetPass(material, "RasterTranslucentT2Pass", stage == 3);

            string renderType = opaque ? "Opaque" : "Transparent";
            if (material.GetTag("RenderType", false) != renderType)
                material.SetOverrideTag("RenderType", renderType);
            if (!opaque && material.renderQueue != (int)RenderQueue.Transparent)
                material.renderQueue = (int)RenderQueue.Transparent;
            else if (opaque && material.renderQueue >= (int)RenderQueue.Transparent)
                material.renderQueue = (int)RenderQueue.Geometry;
        }

        static void SetPass(Material material, string name, bool enabled)
        {
            if (material.FindPass(name) >= 0 && material.GetShaderPassEnabled(name) != enabled)
                material.SetShaderPassEnabled(name, enabled);
        }
    }
}
