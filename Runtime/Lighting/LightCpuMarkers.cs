using System.Collections.Generic;
using Unity.Profiling;

namespace InfinityTech.Rendering.LightPipeline
{
    // CPU stages of light build and shadow allocation. Names stay in this layer.
    internal static class LightCpuMarkers
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

        internal static readonly ProfilerMarker s_Setup = Define("Light.Setup");
        internal static readonly ProfilerMarker s_Registry = Define("Light.Registry");
        internal static readonly ProfilerMarker s_Pack = Define("Light.Pack");
        internal static readonly ProfilerMarker s_TileRect = Define("Light.TileRect");
        internal static readonly ProfilerMarker s_Sort = Define("Light.Sort");
        internal static readonly ProfilerMarker s_ZBin = Define("Light.ZBin");
        internal static readonly ProfilerMarker s_Contact = Define("Light.Contact");
        internal static readonly ProfilerMarker s_EnsureCapacity = Define("Light.EnsureCapacity");
        internal static readonly ProfilerMarker s_CollectCasters = Define("Shadow.CollectCasters");
        internal static readonly ProfilerMarker s_FitCascade = Define("Shadow.FitCascade");
        internal static readonly ProfilerMarker s_AllocateLocal = Define("Shadow.AllocateLocal");
    }
}
