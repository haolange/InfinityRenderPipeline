using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.MeshPipeline
{
    // One owner for Mesh pass samplers and draw marker names. Shader LightModes are separate contracts.
    internal static class MeshDrawProfiling
    {
        static readonly ProfilingSampler s_Culling = new ProfilingSampler("ComputeMeshCulling");
        static readonly ProfilingSampler s_Compaction = new ProfilingSampler("ComputeMeshCompaction");
        static readonly ProfilingSampler s_Upload = new ProfilingSampler("UploadMeshData");
        static readonly ProfilingSampler[] s_Passes = { s_Culling, s_Compaction, s_Upload };

        internal static IReadOnlyList<ProfilingSampler> passes => s_Passes;
        internal static ProfilingSampler culling => s_Culling;
        internal static ProfilingSampler compaction => s_Compaction;
        internal static ProfilingSampler upload => s_Upload;

        internal static readonly ProfilerMarker cpuCleanup = new ProfilerMarker("MeshCPU.CleanupComplete");

        static readonly Dictionary<string, ProfilerMarker> s_Markers = new Dictionary<string, ProfilerMarker>();

        internal static ProfilerMarker Marker(string name)
        {
            if (!s_Markers.TryGetValue(name, out ProfilerMarker marker))
            {
                marker = new ProfilerMarker(name);
                s_Markers.Add(name, marker);
            }
            return marker;
        }

        internal static string PassToken(string lightMode)
        {
            switch (lightMode)
            {
                case "RasterDepthPass": return "Depth";
                case "RasterGBufferPass": return "GBuffer";
                case "RasterForwardPass": return "Forward";
                case "RasterMotionPass": return "Motion";
                case "RasterShadowPass": return "Shadow";
                case "RasterTranslucentDepthPass": return "TranslucentDepth";
                case "RasterTranslucentT0Pass": return "TranslucentT0";
                case "RasterTranslucentT1Pass": return "TranslucentT1";
                case "RasterTranslucentT2Pass": return "TranslucentT2";
                default: return lightMode;
            }
        }

        internal static string Consumer(in MeshView view, string lightMode)
        {
            if (view.kind == EMeshViewKind.CascadeShadow)
                return "CascadeShadow.Slice" + view.subviewIndex;
            if (view.kind == EMeshViewKind.LocalShadow)
                return "LocalShadow.Slice" + view.subviewIndex;
            string pass = PassToken(lightMode);
            if (pass == "Depth" && view.visibilityPhase == EMeshVisibilityPhase.Prediction)
                return "DepthPrediction";
            if (pass == "Depth" && view.visibilityPhase == EMeshVisibilityPhase.Recovered)
                return "DepthRecovery";
            return pass;
        }

        internal static string FrustumGroup(bool depth, bool shadows)
        {
            if (depth && shadows)
                return "DepthAndShadows";
            return shadows ? "Shadows" : "Depth";
        }

        internal static string ShadowGroup(EMeshViewKind kind)
            => kind == EMeshViewKind.LocalShadow ? "LocalShadow" : "CascadeShadow";

        internal static ProfilerMarker Cpu(string stage, in MeshView view, string lightMode)
            => Marker("MeshCPU." + stage + "." + Consumer(view, lightMode));

        internal static string Slice(EMeshViewKind kind, int index)
        {
            if (kind != EMeshViewKind.CascadeShadow && kind != EMeshViewKind.LocalShadow)
                throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "Slice markers belong to shadow views.");
            return "Slice" + index;
        }

        static bool IsShadow(EMeshViewKind kind)
            => kind == EMeshViewKind.CascadeShadow || kind == EMeshViewKind.LocalShadow;

        internal static string Draw(EMeshViewKind kind, bool indirect)
            => IsShadow(kind)
                ? (indirect ? "Shadows.DrawMeshIndirect" : "Shadows.DrawMeshDirect")
                : (indirect ? "RenderLoop.DrawMeshIndirect" : "RenderLoop.DrawMeshDirect");
    }
}
