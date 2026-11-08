using System;
using System.Collections.Generic;
using InfinityTech.Core;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine;

namespace InfinityTech.Component
{
    public partial class MeshComponent
    {
        /// <summary>
        /// Geometry of one <see cref="MeshAsset"/>, shared by the components that reference it: per-level mesh identities
        /// and submesh counts, the union of the level bounds and the element table. A notification marks it stale; the
        /// next reference refresh reads the asset again under a new revision. Arrays are replaced, never edited in place.
        /// </summary>
        sealed class GeometryState
        {
            static uint s_Revision;

            internal MeshAsset asset;
            internal int references;
            internal bool stale = true;
            internal uint revision;
            internal Bounds bounds;
            internal Mesh[] meshes;
            internal ulong[] meshIds;
            internal int[] subMeshCounts;
            /// <summary>Element table: one entry per (level, submesh), in level order.</summary>
            internal int[] elementLod, elementSubmesh, elementSlot;
            /// <summary>Distinct material slots that some element draws with.</summary>
            internal int[] drawnSlots;

            /// <summary>Reads what the asset holds. An invalid asset is read as far as it goes; validation reports it.</summary>
            internal void Refresh()
            {
                stale = false;
                unchecked { revision = ++s_Revision; }
                ReadOnlySpan<Mesh> source = asset.Meshes;
                int levels = Math.Min(Math.Min(source.Length, asset.lodCount), MeshLodChain.MaxLevels);
                var levelMeshes = new Mesh[levels];
                var ids = new ulong[levels];
                var counts = new int[levels];
                int elementCount = 0;
                bool hasBounds = false;
                Bounds union = default;
                for (int i = 0; i < levels; ++i)
                {
                    Mesh mesh = source[i];
                    if (mesh == null) continue;
                    levelMeshes[i] = mesh;
                    ids[i] = UnityEntityId.ToUInt64(mesh);
                    counts[i] = mesh.subMeshCount;
                    elementCount += counts[i];
                    if (hasBounds) union.Encapsulate(mesh.bounds);
                    else { union = mesh.bounds; hasBounds = true; }
                }
                var lod = new int[elementCount];
                var submesh = new int[elementCount];
                var slot = new int[elementCount];
                var drawn = new List<int>();
                for (int i = 0, k = 0; i < levels; ++i)
                {
                    for (int s = 0; s < counts[i]; ++s, ++k)
                    {
                        lod[k] = i;
                        submesh[k] = s;
                        slot[k] = asset.GetMaterialSlot(i, s);
                        if (slot[k] >= 0 && !drawn.Contains(slot[k])) drawn.Add(slot[k]);
                    }
                }
                meshes = levelMeshes; meshIds = ids; subMeshCounts = counts; bounds = union;
                elementLod = lod; elementSubmesh = submesh; elementSlot = slot; drawnSlots = drawn.ToArray();
            }
        }

        static readonly Dictionary<MeshAsset, GeometryState> s_Geometry = new Dictionary<MeshAsset, GeometryState>();
        /// <summary>Live shared geometry states; one per asset that a registered component references.</summary>
        internal static int geometryStateCount => s_Geometry.Count;
        GeometryState m_Geometry;
        MeshAsset m_RetainedAsset;

        /// <summary>A registered component holds one reference on its asset's geometry state; an unregistered one holds none.</summary>
        void RefreshGeometryReference()
        {
            if (!s_Registered.Contains(this)) return;
            if (!ReferenceEquals(m_RetainedAsset, asset) || m_Geometry == null)
            {
                ReleaseGeometryReference();
                if (asset == null) return;
                if (!s_Geometry.TryGetValue(asset, out m_Geometry))
                {
                    m_Geometry = new GeometryState { asset = asset };
                    s_Geometry.Add(asset, m_Geometry);
                }
                m_RetainedAsset = asset; ++m_Geometry.references;
            }
            if (m_Geometry.stale && asset != null) m_Geometry.Refresh();
        }

        void ReleaseGeometryReference()
        {
            if (m_Geometry != null && --m_Geometry.references == 0) s_Geometry.Remove(m_RetainedAsset);
            m_RetainedAsset = null; m_Geometry = null;
        }

        static void InvalidateGeometry(GeometryState state)
        {
            state.stale = true;
            foreach (var component in s_Registered)
                if (ReferenceEquals(component.m_Geometry, state)) component.MarkDirty();
        }
    }
}
