using System.Collections.Generic;
using InfinityTech.Rendering;
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

        static readonly List<ProfilingSampler> s_CascadeConsumers = new List<ProfilingSampler>(4);
        static readonly List<ProfilingSampler> s_LocalConsumers = new List<ProfilingSampler>(8);
        static readonly List<ProfilingSampler> s_Slices = new List<ProfilingSampler>(8);

        static ProfilingSampler Named(List<ProfilingSampler> cache, int index, string prefix)
        {
            if (index < 0)
                throw new System.ArgumentOutOfRangeException(nameof(index), index, prefix + " marker index is out of range.");
            while (cache.Count <= index)
            {
                cache.Add(null);
            }
            return cache[index] ?? (cache[index] = new ProfilingSampler(prefix + index));
        }

        internal static ProfilingSampler SliceSampler(int index) => Named(s_Slices, index, "Slice");

        internal static ProfilingSampler ConsumerSampler(in MeshView view, string lightMode)
        {
            if (view.kind == EMeshViewKind.CascadeShadow)
                return Named(s_CascadeConsumers, view.subviewIndex, "CascadeShadow.Slice");
            if (view.kind == EMeshViewKind.LocalShadow)
                return Named(s_LocalConsumers, view.subviewIndex, "LocalShadow.Slice");
            string pass = PassToken(lightMode);
            if (pass == "Depth" && view.visibilityPhase == EMeshVisibilityPhase.Prediction)
                return CachedSampler.Get("DepthPrediction");
            if (pass == "Depth" && view.visibilityPhase == EMeshVisibilityPhase.Recovered)
                return CachedSampler.Get("DepthRecovery");
            return CachedSampler.Get(pass);
        }

        readonly struct CpuMarkerKey : System.IEquatable<CpuMarkerKey>
        {
            public readonly string stage;
            public readonly string token;
            public readonly byte kind;
            public readonly byte index;
            public readonly byte phase;

            public CpuMarkerKey(string stage, string token, EMeshViewKind kind, int index, EMeshVisibilityPhase phase)
            {
                this.stage = stage;
                this.token = token;
                this.kind = (byte)kind;
                this.index = (byte)index;
                this.phase = (byte)phase;
            }

            public bool Equals(CpuMarkerKey other)
                => stage == other.stage && token == other.token && kind == other.kind && index == other.index && phase == other.phase;

            public override bool Equals(object obj) => obj is CpuMarkerKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = stage != null ? stage.GetHashCode() : 0;
                    hash = hash * 31 + (token != null ? token.GetHashCode() : 0);
                    hash = hash * 31 + kind;
                    hash = hash * 31 + index;
                    return hash * 31 + phase;
                }
            }
        }

        static readonly Dictionary<CpuMarkerKey, ProfilerMarker> s_CpuMarkers = new Dictionary<CpuMarkerKey, ProfilerMarker>(32);

        internal static ProfilerMarker Cpu(string stage, in MeshView view, string lightMode)
        {
            string token = view.kind == EMeshViewKind.CascadeShadow || view.kind == EMeshViewKind.LocalShadow
                ? null : PassToken(lightMode);
            var key = new CpuMarkerKey(stage, token, view.kind, view.subviewIndex, view.visibilityPhase);
            if (!s_CpuMarkers.TryGetValue(key, out ProfilerMarker marker))
            {
                marker = new ProfilerMarker("MeshCPU." + stage + "." + Consumer(view, lightMode));
                s_CpuMarkers.Add(key, marker);
            }
            return marker;
        }

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
