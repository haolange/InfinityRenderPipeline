using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.MeshPipeline
{
    // One owner for Mesh pass samplers and draw marker names. Shader LightModes are separate contracts.
    internal static class MeshDrawProfiling
    {
        static readonly ProfilingSampler s_Production = new ProfilingSampler("ComputeMeshDrawList");
        static readonly ProfilingSampler s_Upload = new ProfilingSampler("UploadMeshData");
        static readonly ProfilingSampler[] s_Passes = { s_Production, s_Upload };

        /// <summary>Graph pass samplers: the GPU producer and the CPU upload of a draw list.</summary>
        internal static IReadOnlyList<ProfilingSampler> passes => s_Passes;
        internal static ProfilingSampler production => s_Production;
        internal static ProfilingSampler upload => s_Upload;

        internal static readonly ProfilerMarker cpuCleanup = new ProfilerMarker("MeshCPU.CleanupComplete");
        internal static readonly ProfilingSampler initializeIndirectArgs = new ProfilingSampler("InitializeIndirectArgs");
        internal static readonly ProfilingSampler buildDrawList = new ProfilingSampler("BuildDrawList");

        static readonly ShaderTagId s_ShadowPass = new ShaderTagId("RasterShadowPass");
        static readonly ProfilingSampler s_ShadowDrawDirect = new ProfilingSampler("Shadows.DrawMeshDirect");
        static readonly ProfilingSampler s_ShadowDrawIndirect = new ProfilingSampler("Shadows.DrawMeshIndirect");
        static readonly ProfilingSampler s_RenderLoopDrawDirect = new ProfilingSampler("RenderLoop.DrawMeshDirect");
        static readonly ProfilingSampler s_RenderLoopDrawIndirect = new ProfilingSampler("RenderLoop.DrawMeshIndirect");

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

        static readonly List<ProfilingSampler> s_Slices = new List<ProfilingSampler>(8);

        internal static ProfilingSampler SliceSampler(int index)
        {
            if (index < 0)
                throw new System.ArgumentOutOfRangeException(nameof(index), index, "Slice marker index is out of range.");
            while (s_Slices.Count <= index)
            {
                s_Slices.Add(null);
            }
            return s_Slices[index] ?? (s_Slices[index] = new ProfilingSampler("Slice" + index));
        }

        // A pass cache resolves its sampler and CPU markers once, named by the PassToken of its LightMode.
        internal static ProfilingSampler PassSampler(string lightMode) => new ProfilingSampler(PassToken(lightMode));
        internal static ProfilerMarker CpuMarker(string stage, string lightMode) => new ProfilerMarker("MeshCPU." + stage + "." + PassToken(lightMode));

        /// <summary>Shadow-pass draws sit under <c>Shadows.*</c>, every other pass under <c>RenderLoop.*</c>.</summary>
        internal static ProfilingSampler DrawSampler(in MeshPassDesc desc, bool indirect)
            => desc.passTag == s_ShadowPass
                ? (indirect ? s_ShadowDrawIndirect : s_ShadowDrawDirect)
                : (indirect ? s_RenderLoopDrawIndirect : s_RenderLoopDrawDirect);
    }
}
