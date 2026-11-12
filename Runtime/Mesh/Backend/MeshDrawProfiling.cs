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

        internal static readonly ProfilingSampler depthPrediction = new ProfilingSampler("DepthPrediction");
        internal static readonly ProfilingSampler depthRecovery = new ProfilingSampler("DepthRecovery");
        internal static readonly ProfilingSampler initializeIndirectArgs = new ProfilingSampler("InitializeIndirectArgs");
        internal static readonly ProfilingSampler compact = new ProfilingSampler("Compact");
        static readonly ProfilingSampler s_DepthPass = new ProfilingSampler("Depth");
        static readonly ProfilingSampler s_CascadeShadowGroup = new ProfilingSampler("CascadeShadow");
        static readonly ProfilingSampler s_LocalShadowGroup = new ProfilingSampler("LocalShadow");
        static readonly ProfilingSampler s_FrustumDepthAndShadows = new ProfilingSampler("DepthAndShadows");
        static readonly ProfilingSampler s_FrustumShadows = new ProfilingSampler("Shadows");
        static readonly ProfilingSampler s_ShadowDrawDirect = new ProfilingSampler("Shadows.DrawMeshDirect");
        static readonly ProfilingSampler s_ShadowDrawIndirect = new ProfilingSampler("Shadows.DrawMeshIndirect");
        static readonly ProfilingSampler s_RenderLoopDrawDirect = new ProfilingSampler("RenderLoop.DrawMeshDirect");
        static readonly ProfilingSampler s_RenderLoopDrawIndirect = new ProfilingSampler("RenderLoop.DrawMeshIndirect");

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

        internal static ProfilingSampler FrustumGroupSampler(bool depth, bool shadows)
        {
            if (depth && shadows)
                return s_FrustumDepthAndShadows;
            return shadows ? s_FrustumShadows : s_DepthPass;
        }

        internal static ProfilingSampler ShadowGroupSampler(EMeshViewKind kind)
            => kind == EMeshViewKind.LocalShadow ? s_LocalShadowGroup : s_CascadeShadowGroup;

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

        // A pass cache resolves its sampler once, named by the PassToken of its LightMode.
        internal static ProfilingSampler PassSampler(string lightMode)
            => lightMode == "RasterDepthPass" ? s_DepthPass : new ProfilingSampler(PassToken(lightMode));

        internal static ProfilingSampler ConsumerSampler(in MeshView view, ProfilingSampler passSampler)
        {
            if (view.kind == EMeshViewKind.CascadeShadow)
                return Named(s_CascadeConsumers, view.subviewIndex, "CascadeShadow.Slice");
            if (view.kind == EMeshViewKind.LocalShadow)
                return Named(s_LocalConsumers, view.subviewIndex, "LocalShadow.Slice");
            bool depth = ReferenceEquals(passSampler, s_DepthPass);
            if (depth && view.visibilityPhase == EMeshVisibilityPhase.Prediction)
                return depthPrediction;
            if (depth && view.visibilityPhase == EMeshVisibilityPhase.Recovered)
                return depthRecovery;
            return passSampler;
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

        static bool IsShadow(EMeshViewKind kind)
            => kind == EMeshViewKind.CascadeShadow || kind == EMeshViewKind.LocalShadow;

        internal static ProfilingSampler DrawSampler(EMeshViewKind kind, bool indirect)
            => IsShadow(kind)
                ? (indirect ? s_ShadowDrawIndirect : s_ShadowDrawDirect)
                : (indirect ? s_RenderLoopDrawIndirect : s_RenderLoopDrawDirect);
    }
}
