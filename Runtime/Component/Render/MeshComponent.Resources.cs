using System.Collections.Generic;
using InfinityTech.Core;
using UnityEngine;

namespace InfinityTech.Component
{
    public partial class MeshComponent
    {
        sealed class GeometryState
        {
            internal ulong identity;
            internal int subMeshes, references;
            internal uint revision;
            internal Bounds bounds;
        }
        static readonly Dictionary<Mesh, GeometryState> s_Geometry = new Dictionary<Mesh, GeometryState>();
        GeometryState m_Geometry;
        Mesh m_RetainedMesh;

        void RefreshGeometryReference()
        {
            if (ReferenceEquals(m_RetainedMesh, meshAsset) && m_Geometry != null) return;
            ReleaseGeometryReference();
            if (meshAsset == null) return;
            if (!s_Geometry.TryGetValue(meshAsset, out m_Geometry))
            {
                m_Geometry = new GeometryState { identity = UnityEntityId.ToUInt64(meshAsset), subMeshes = meshAsset.subMeshCount, bounds = meshAsset.bounds, revision = 1 };
                s_Geometry.Add(meshAsset, m_Geometry);
            }
            m_RetainedMesh = meshAsset; ++m_Geometry.references;
            m_GeometryRevision = m_Geometry.revision; localBounds = m_Geometry.bounds;
        }

        void ReleaseGeometryReference()
        {
            if (m_Geometry != null && --m_Geometry.references == 0) s_Geometry.Remove(m_RetainedMesh);
            m_RetainedMesh = null; m_Geometry = null;
        }

        static void RefreshSharedGeometry(Mesh mesh)
        {
            if (mesh == null || !s_Geometry.TryGetValue(mesh, out var state)) return;
            state.subMeshes = mesh.subMeshCount; state.bounds = mesh.bounds;
            unchecked { ++state.revision; }
            foreach (var component in s_Registered)
                if (ReferenceEquals(component.m_RetainedMesh, mesh))
                {
                    component.m_GeometryRevision = state.revision;
                    component.localBounds = state.bounds;
                    component.MarkTransformDirty(); component.MarkDirty();
                }
        }
    }
}
