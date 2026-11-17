using System.Collections.Generic;
using Unity.Profiling;

namespace InfinityTech.Rendering.RenderGraph
{
    // CPU stages of graph compile and execute. Names do not mention a pipeline or a mesh.
    internal static class RGCpuMarkers
    {
        static readonly List<string> s_Names = new List<string>();
        static readonly List<ProfilerMarker> s_Markers = new List<ProfilerMarker>();

        static ProfilerMarker Define(string name)
        {
            ProfilerMarker marker = new ProfilerMarker(name);
            s_Names.Add(name);
            s_Markers.Add(marker);
            return marker;
        }

        internal static IReadOnlyList<string> Names => s_Names;
        internal static IReadOnlyList<ProfilerMarker> Markers => s_Markers;

        internal static readonly ProfilerMarker s_CompilePlace = Define("RG.Compile.Place");
        internal static readonly ProfilerMarker s_CompileCull = Define("RG.Compile.Cull");
        internal static readonly ProfilerMarker s_CompileContribute = Define("RG.Compile.Contribute");
        internal static readonly ProfilerMarker s_CompileRendererLists = Define("RG.Compile.RendererLists");
        internal static readonly ProfilerMarker s_CompileLifetime = Define("RG.Compile.Lifetime");
        internal static readonly ProfilerMarker s_CompileNativeGroups = Define("RG.Compile.NativeGroups");
        internal static readonly ProfilerMarker s_CompileSegments = Define("RG.Compile.Segments");
        internal static readonly ProfilerMarker s_CompileProfile = Define("RG.Compile.Profile");
        internal static readonly ProfilerMarker s_CompileValidate = Define("RG.Compile.Validate");
        internal static readonly ProfilerMarker s_ExecutePrepare = Define("RG.Execute.Prepare");
        internal static readonly ProfilerMarker s_ExecuteRendererLists = Define("RG.Execute.RendererLists");
        internal static readonly ProfilerMarker s_ExecuteSubmit = Define("RG.Execute.Submit");
        internal static readonly ProfilerMarker s_ExecuteCreate = Define("RG.Execute.Create");
        internal static readonly ProfilerMarker s_ExecuteRelease = Define("RG.Execute.Release");
        internal static readonly ProfilerMarker s_ExecuteClear = Define("RG.Execute.Clear");
    }
}
