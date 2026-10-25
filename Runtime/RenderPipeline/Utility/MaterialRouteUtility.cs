using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public static class MaterialRouteUtility
    {
        sealed class MeshRoute
        {
            internal int route, stage, queue;
            internal uint passMask;
            internal uint version = 1;
        }
        static readonly string[] s_MeshPasses = { "RasterDepthPass", "RasterGBufferPass", "RasterForwardPass", "RasterShadowPass", "RasterMotionPass", "RasterTranslucentDepthPass", "RasterTranslucentT0Pass", "RasterTranslucentT1Pass", "RasterTranslucentT2Pass" };
        static uint PassMask(Material material)
        {
            uint mask = 0;
            for (int i = 0; i < s_MeshPasses.Length; ++i) if (material.GetShaderPassEnabled(s_MeshPasses[i])) mask |= 1u << i;
            return mask;
        }
        static readonly ConditionalWeakTable<Material, MeshRoute>.CreateValueCallback s_CreateMeshRoute = CaptureMeshRoute;
        static readonly ConditionalWeakTable<Material, MeshRoute> s_MeshRoutes = new ConditionalWeakTable<Material, MeshRoute>();
        static MeshRoute CaptureMeshRoute(Material material)
        {
            Read(material, out int route, out int stage);
            return new MeshRoute { route = route, stage = stage, queue = material.renderQueue, passMask = PassMask(material) };
        }
        internal static void ReadMeshRoute(Material material, out int route, out int stage, out int queue, out uint version)
        {
            var state = s_MeshRoutes.GetValue(material, s_CreateMeshRoute);
            route = state.route; stage = state.stage; queue = state.queue; version = state.version;
        }

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
            NotifyRoutingChanged(material);
        }

        public static void NotifyRoutingChanged(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            var state = s_MeshRoutes.GetValue(material, s_CreateMeshRoute);
            Read(material, out int route, out int stage);
            int queue = material.renderQueue; uint mask = PassMask(material);
            if (state.route == route && state.stage == stage && state.queue == queue && state.passMask == mask) return;
            state.route = route; state.stage = stage; state.queue = queue; state.passMask = mask;
            unchecked { ++state.version; }
            InfinityTech.Component.MeshComponent.InvalidateMaterialRouting(material);
        }

        static void SetPass(Material material, string name, bool enabled)
        {
            if (material.FindPass(name) >= 0 && material.GetShaderPassEnabled(name) != enabled)
                material.SetShaderPassEnabled(name, enabled);
        }
    }
}
