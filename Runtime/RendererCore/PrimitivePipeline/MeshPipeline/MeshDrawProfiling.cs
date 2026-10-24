using System.Collections.Generic;
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

        static bool IsShadow(EMeshViewKind kind)
            => kind == EMeshViewKind.CascadeShadow || kind == EMeshViewKind.LocalShadow;

        internal static string Draw(EMeshViewKind kind, bool indirect)
            => IsShadow(kind)
                ? (indirect ? "Shadows.DrawMeshIndirect" : "Shadows.DrawMeshDirect")
                : (indirect ? "RenderLoop.DrawMeshIndirect" : "RenderLoop.DrawMeshDirect");
    }
}
