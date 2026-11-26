using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.MaterialPipeline
{
    public static class MaterialRouteUtility
    {
        private sealed class MeshRoute
        {
            internal int route, stage, queue;
            internal Shader shader;
        }
        private static readonly string[] s_RoutedPasses = { "RasterGBufferPass", "RasterForwardPass", "RasterDepthPass", "RasterShadowPass", "ShadowCaster", "RasterMotionPass",
            "RasterTranslucentDepthPass", "RasterTranslucentT0Pass", "RasterTranslucentT1Pass", "RasterTranslucentT2Pass" };

        /// <summary>
        /// The one authority for which passes a surface route and translucent stage draw. Pass state on the material and
        /// mesh-pass membership both derive from it.
        /// </summary>
        public static bool RouteEnablesPass(int route, int stage, string lightMode)
        {
            bool opaque = stage == 0;
            switch (lightMode)
            {
                case "RasterGBufferPass":
                    return opaque;
                case "RasterForwardPass":
                    return opaque && route == 1;
                case "RasterDepthPass":
                case "RasterShadowPass":
                case "ShadowCaster":
                case "RasterMotionPass":
                    return opaque;
                case "RasterTranslucentDepthPass":
                    return !opaque;
                case "RasterTranslucentT0Pass":
                    return stage == 1;
                case "RasterTranslucentT1Pass":
                    return stage == 2;
                case "RasterTranslucentT2Pass":
                    return stage == 3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lightMode), lightMode, "Not a routed pass.");
            }
        }
        private static readonly ConditionalWeakTable<Material, MeshRoute>.CreateValueCallback s_CreateMeshRoute = CaptureMeshRoute;
        private static readonly ConditionalWeakTable<Material, MeshRoute> s_MeshRoutes = new ConditionalWeakTable<Material, MeshRoute>();

        private static MeshRoute CaptureMeshRoute(Material material)
        {
            Read(material, out int route, out int stage);
            return new MeshRoute { route = route, stage = stage, queue = material.renderQueue, shader = material.shader };
        }
        internal static void ReadMeshRoute(Material material, out int route, out int stage, out int queue, out Shader shader)
        {
            var state = s_MeshRoutes.GetValue(material, s_CreateMeshRoute);
            route = state.route;
            stage = state.stage;
            queue = state.queue;
            shader = state.shader;
        }

        private static readonly int ID_SurfaceRoute          = Shader.PropertyToID("_SurfaceRoute");
        private static readonly int ID_TranslucentStage      = Shader.PropertyToID("_TranslucentStage");
        private static readonly int ID_DiffusionProfileIndex = Shader.PropertyToID("_DiffusionProfileIndex");
        private static readonly int ID_StencilRefGBuffer     = Shader.PropertyToID("_StencilRefGBuffer");

        // Stencil the GBuffer pass writes under WriteMask 6 (Shaders/Library/Stencil.hlsl): bit 1 admits a deferred pixel to
        // deferred shading. A skin shader writes its subsurface bit 2 as a constant. Forward clears both.
        private const float k_StencilRefDeferred = InfinityTech.Rendering.StencilBits.Deferred;
        private const float k_StencilRefForward  = 0.0f;

        public static void Read(Material material, out int surfaceRoute, out int translucentStage)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));

            surfaceRoute = ReadIndex(material, ID_SurfaceRoute, "_SurfaceRoute", 1);
            translucentStage = ReadIndex(material, ID_TranslucentStage, "_TranslucentStage", 3);
            if (material.HasProperty(ID_DiffusionProfileIndex) && (surfaceRoute != 1 || translucentStage != 0))
                throw new InvalidOperationException($"Material '{material.name}' is a subsurface skin material and requires Forward opaque routing.");
        }

        private static int ReadIndex(Material material, int property, string name, int maximum)
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
            // Both opaque routes write the GBuffer surface proxy for screen-space, ray-tracing and motion consumers.
            // Only a deferred pixel carries the stencil bit that admits it to deferred shading.
            foreach (string pass in s_RoutedPasses)
            {
                SetPass(material, pass, RouteEnablesPass(route, stage, pass));
            }
            if (material.HasProperty(ID_StencilRefGBuffer))
            {
                float stencilRef = route == 0 ? k_StencilRefDeferred : k_StencilRefForward;
                if (material.GetFloat(ID_StencilRefGBuffer) != stencilRef)
                    material.SetFloat(ID_StencilRefGBuffer, stencilRef);
            }

            string renderType = opaque ? "Opaque" : "Transparent";
            if (material.GetTag("RenderType", false) != renderType)
                material.SetOverrideTag("RenderType", renderType);
            if (!opaque && material.renderQueue != (int)RenderQueue.Transparent)
                material.renderQueue = (int)RenderQueue.Transparent;
            else if (opaque && material.renderQueue >= (int)RenderQueue.Transparent)
                material.renderQueue = (int)RenderQueue.Geometry;
            NotifyRoutingChanged(material);
        }

        // Raised once per effective route, stage, queue or shader change; mesh components invalidate their draw structure.
        internal static event Action<Material> routingChanged;

        public static void NotifyRoutingChanged(Material material)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }
            var state = s_MeshRoutes.GetValue(material, s_CreateMeshRoute);
            Read(material, out int route, out int stage);
            int queue = material.renderQueue;
            Shader shader = material.shader;
            if (state.route == route && state.stage == stage && state.queue == queue && state.shader == shader)
            {
                return;
            }
            state.route = route;
            state.stage = stage;
            state.queue = queue;
            state.shader = shader;
            routingChanged?.Invoke(material);
        }

        internal static void SetNamedPass(Material material, string name, bool enabled)
        {
            if (material.FindPass(name) >= 0 && material.GetShaderPassEnabled(name) != enabled)
                material.SetShaderPassEnabled(name, enabled);
        }

        private static void SetPass(Material material, string name, bool enabled)
        {
            SetNamedPass(material, name, enabled);
        }
    }
}
