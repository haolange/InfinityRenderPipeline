using System.Collections.Generic;
using Unity.Profiling;

namespace InfinityTech.Rendering.MeshPipeline
{
    // CPU stages of mesh residency and draw-list recording. GPU pass samplers stay on MeshDrawProfiling.
    internal static class MeshCpuMarkers
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

        internal static readonly ProfilerMarker s_UpdateMeshWorldGPU = Define("UpdateMeshWorldGPU");
        internal static readonly ProfilerMarker s_ResidencyGate = Define("Mesh.ResidencyGate");
        internal static readonly ProfilerMarker s_PrepareTransformVersion = Define("Mesh.PrepareTransformVersion");
        internal static readonly ProfilerMarker s_CollectDirtyRuns = Define("Mesh.CollectDirtyRuns");
        internal static readonly ProfilerMarker s_UploadColumns = Define("Mesh.UploadColumns");
        internal static readonly ProfilerMarker s_CanWriteBuffer = Define("Mesh.CanWriteBuffer");
        internal static readonly ProfilerMarker s_RecreateColumns = Define("Mesh.RecreateColumns");
        internal static readonly ProfilerMarker s_DeclareList = Define("Mesh.DeclareList");
        internal static readonly ProfilerMarker s_RecordProducer = Define("Mesh.RecordProducer");
    }
}
