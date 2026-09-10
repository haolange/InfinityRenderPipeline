using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.MeshPipeline
{
    [StructLayout(LayoutKind.Sequential)]
    public struct FMeshBakedLighting : IEquatable<FMeshBakedLighting>
    {
        public float4 metadata, scaleOffset;
        public float4 shAr, shAg, shAb, shBr, shBg, shBb, shC;
        public float4 occlusion;
        public int TextureSet => (int)metadata.x;
        public bool Equals(FMeshBakedLighting other) => math.all(metadata == other.metadata) && math.all(scaleOffset == other.scaleOffset)
            && math.all(shAr == other.shAr) && math.all(shAg == other.shAg) && math.all(shAb == other.shAb)
            && math.all(shBr == other.shBr) && math.all(shBg == other.shBg) && math.all(shBb == other.shBb)
            && math.all(shC == other.shC) && math.all(occlusion == other.occlusion);
    }

    internal static class MeshBakedLighting
    {
        static readonly Vector3[] s_Positions = new Vector3[1];
        static readonly SphericalHarmonicsL2[] s_Probes = new SphericalHarmonicsL2[1];
        static readonly Vector4[] s_Occlusion = new Vector4[1];
        static readonly GlobalKeyword s_Lightmap = GlobalKeyword.Create("INFINITY_MESH_LIGHTMAP");
        static readonly GlobalKeyword s_Directional = GlobalKeyword.Create("INFINITY_MESH_DIRECTIONAL");
        static readonly GlobalKeyword s_Shadowmask = GlobalKeyword.Create("INFINITY_MESH_SHADOWMASK");
        internal static readonly int BufferID = Shader.PropertyToID("meshBakedLightingBuffer");

        // Called on the main thread while collecting entity changes, never by a Burst job.
        internal static FMeshBakedLighting Capture(Renderer source, Vector3 position)
        {
            FMeshBakedLighting data = default;
            data.occlusion = new float4(1);
            int index = source != null ? source.lightmapIndex : -1;
            if (index >= 0 && index < 65534)
            {
                var maps = LightmapSettings.lightmaps;
                if (index >= maps.Length || maps[index].lightmapColor == null)
                    throw new InvalidOperationException("A Mesh lighting source references a missing Unity lightmap: " + index);
                data.metadata.x = index + 1;
                Vector4 st = source.lightmapScaleOffset;
                data.scaleOffset = new float4(st.x, st.y, st.z, st.w);
            }
            if (data.TextureSet == 0 && LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0 &&
                (source == null || source.lightProbeUsage != LightProbeUsage.Off))
            {
                s_Positions[0] = source != null ? (source.probeAnchor != null ? source.probeAnchor.position : source.bounds.center) : position;
                LightProbes.CalculateInterpolatedLightAndOcclusionProbes(s_Positions, s_Probes, s_Occlusion);
                SphericalHarmonicsL2 sh = s_Probes[0];
                data.metadata.y = 1;
                data.shAr = PackA(sh, 0); data.shAg = PackA(sh, 1); data.shAb = PackA(sh, 2);
                data.shBr = PackB(sh, 0); data.shBg = PackB(sh, 1); data.shBb = PackB(sh, 2);
                data.shC = new float4(sh[0, 8], sh[1, 8], sh[2, 8], 1);
                Vector4 occlusion = s_Occlusion[0];
                data.occlusion = new float4(occlusion.x, occlusion.y, occlusion.z, occlusion.w);
            }
            return data;
        }
        static float4 PackA(SphericalHarmonicsL2 sh, int c) => new float4(sh[c, 3], sh[c, 1], sh[c, 2], sh[c, 0] - sh[c, 6]);
        static float4 PackB(SphericalHarmonicsL2 sh, int c) => new float4(sh[c, 4], sh[c, 5], sh[c, 6] * 3, sh[c, 7]);

        internal static void Bind(CommandBuffer commands, MaterialPropertyBlock properties, int textureSet, ComputeBuffer data)
        {
            properties.SetBuffer(BufferID, data);
            bool mapped = textureSet > 0;
            var maps = LightmapSettings.lightmaps;
            if (mapped && (textureSet > maps.Length || maps[textureSet - 1].lightmapColor == null))
                throw new InvalidOperationException("A live Mesh draw lost its baked texture set.");
            LightmapData map = mapped ? maps[textureSet - 1] : null;
            bool directional = mapped && LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
            if (directional && map.lightmapDir == null)
                throw new InvalidOperationException("Directional lightmap mode requires a direction texture.");
            commands.SetKeyword(s_Lightmap, mapped);
            commands.SetKeyword(s_Directional, directional);
            commands.SetKeyword(s_Shadowmask, mapped && map.shadowMask != null);
            if (mapped) properties.SetTexture("_MeshLightmapColor", map.lightmapColor);
            if (directional) properties.SetTexture("_MeshLightmapDirection", map.lightmapDir);
            if (mapped && map.shadowMask != null) properties.SetTexture("_MeshShadowMask", map.shadowMask);
        }
        internal static void ClearKeywords(CommandBuffer commands)
        {
            commands.SetKeyword(s_Lightmap, false); commands.SetKeyword(s_Directional, false); commands.SetKeyword(s_Shadowmask, false);
        }
    }
}
