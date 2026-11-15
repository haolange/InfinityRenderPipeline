using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// How a culling context measures screen size for LOD selection: <c>screen = largest half-extent * scale * bias / distance</c>,
    /// with distance 1 when orthographic. A context that draws for a camera (a shadow cascade included) copies that camera's
    /// projection, so casters use the level the camera draws.
    /// </summary>
    public struct MeshLODProjection : IEquatable<MeshLODProjection>
    {
        public float3 origin;
        public float scale;
        public float bias;
        public bool orthographic;

        /// <summary>Throws unless the origin is finite and the scale and bias are finite and positive.</summary>
        public void ThrowIfInvalid()
        {
            if (!math.all(math.isfinite(origin)))
            {
                throw new ArgumentException($"LOD origin {origin} is not finite.");
            }
            if (!(scale > 0 && math.isfinite(scale)))
            {
                throw new ArgumentOutOfRangeException(nameof(scale), scale, "The LOD scale is finite and positive.");
            }
            if (!(bias > 0 && math.isfinite(bias)))
            {
                throw new ArgumentOutOfRangeException(nameof(bias), bias, "The LOD bias is finite and positive.");
            }
        }

        public bool Equals(MeshLODProjection other) => origin.Equals(other.origin) && scale == other.scale && bias == other.bias && orthographic == other.orthographic;
        public override bool Equals(object obj) => obj is MeshLODProjection other && Equals(other);
        public override int GetHashCode() => origin.GetHashCode() ^ scale.GetHashCode() ^ (bias.GetHashCode() * 397);
    }

    /// <summary>
    /// What a draw list culls entities against: up to six planes, the layer, scene and rendering-layer masks, and the LOD
    /// projection. The caller builds it from its own view (a camera, a shadow cascade, a light face); the mesh mechanism
    /// only tests entities against it. Field equality is its identity: occluders and repeated draw lists match on it.
    /// Distances for translucent ordering are measured from the LOD origin.
    /// </summary>
    // GPU-RECORD: MeshCullingContext Shaders/Mesh/Compute_MeshDrawPipeline.compute
    [StructLayout(LayoutKind.Sequential)]
    public struct MeshCullingContext : IEquatable<MeshCullingContext>
    {
        public const int MaxPlanes = 6;

        public float4 plane0, plane1, plane2, plane3, plane4, plane5;
        public uint planeCount;
        public uint layerMask;
        public uint sceneLow;
        public uint sceneHigh;
        public uint renderingLayerMask;
        public uint filterRenderingLayers;
        public uint lodOrthographic;
        public float lodScale;
        public float lodBias;
        public float lodOriginX, lodOriginY, lodOriginZ;

        /// <param name="filterRenderingLayers">Whether an entity must share a rendering layer with the mask (light views do; camera views do not).</param>
        public static MeshCullingContext Create(ReadOnlySpan<Plane> planes, int layerMask, ulong sceneCullingMask, uint renderingLayerMask,
            bool filterRenderingLayers, in MeshLODProjection lod)
        {
            if (planes.Length > MaxPlanes)
            {
                throw new ArgumentOutOfRangeException(nameof(planes), planes.Length, $"A culling context holds at most {MaxPlanes} planes.");
            }
            lod.ThrowIfInvalid();
            var context = new MeshCullingContext
            {
                planeCount = (uint)planes.Length,
                layerMask = unchecked((uint)layerMask),
                sceneLow = unchecked((uint)sceneCullingMask),
                sceneHigh = unchecked((uint)(sceneCullingMask >> 32)),
                renderingLayerMask = renderingLayerMask,
                filterRenderingLayers = filterRenderingLayers ? 1u : 0u,
                lodOrthographic = lod.orthographic ? 1u : 0u,
                lodScale = lod.scale,
                lodBias = lod.bias,
                lodOriginX = lod.origin.x,
                lodOriginY = lod.origin.y,
                lodOriginZ = lod.origin.z
            };
            for (int i = 0; i < planes.Length; ++i)
            {
                context.SetPlane(i, new float4(planes[i].normal, planes[i].distance));
            }
            return context;
        }

        internal float3 lodOrigin => new float3(lodOriginX, lodOriginY, lodOriginZ);
        internal ulong sceneCullingMask => sceneLow | ((ulong)sceneHigh << 32);

        internal MeshLODProjection ReadLODProjection()
            => new MeshLODProjection { origin = lodOrigin, scale = lodScale, bias = lodBias, orthographic = lodOrthographic != 0 };

        // index < planeCount <= MaxPlanes, which Create establishes. Burst-compiled jobs call this; it does not throw.
        internal float4 GetPlane(int index)
        {
            switch (index)
            {
                case 0: return plane0;
                case 1: return plane1;
                case 2: return plane2;
                case 3: return plane3;
                case 4: return plane4;
                default: return plane5;
            }
        }

        void SetPlane(int index, float4 plane)
        {
            switch (index)
            {
                case 0: plane0 = plane; break;
                case 1: plane1 = plane; break;
                case 2: plane2 = plane; break;
                case 3: plane3 = plane; break;
                case 4: plane4 = plane; break;
                case 5: plane5 = plane; break;
                default: throw new ArgumentOutOfRangeException(nameof(index), index, "A culling context has six plane slots.");
            }
        }

        public bool Equals(MeshCullingContext other)
        {
            return plane0.Equals(other.plane0) && plane1.Equals(other.plane1) && plane2.Equals(other.plane2)
                && plane3.Equals(other.plane3) && plane4.Equals(other.plane4) && plane5.Equals(other.plane5)
                && planeCount == other.planeCount && layerMask == other.layerMask && sceneLow == other.sceneLow && sceneHigh == other.sceneHigh
                && renderingLayerMask == other.renderingLayerMask && filterRenderingLayers == other.filterRenderingLayers
                && lodOrthographic == other.lodOrthographic && lodScale == other.lodScale && lodBias == other.lodBias
                && lodOriginX == other.lodOriginX && lodOriginY == other.lodOriginY && lodOriginZ == other.lodOriginZ;
        }

        public override bool Equals(object obj) => obj is MeshCullingContext other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = plane0.GetHashCode();
                hash = hash * 31 + plane1.GetHashCode();
                hash = hash * 31 + plane2.GetHashCode();
                hash = hash * 31 + (int)layerMask;
                hash = hash * 31 + (int)sceneLow;
                hash = hash * 31 + (int)renderingLayerMask;
                return hash * 31 + lodOrigin.GetHashCode();
            }
        }
    }
}
