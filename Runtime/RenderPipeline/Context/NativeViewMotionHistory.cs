using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // Native renderer-list submission keeps Unity's culling, LOD, skinning and sorting.
    // Previous rigid and deformed poses belong to the receiving view's committed history.
    internal sealed class NativeViewMotionHistory
    {
        static readonly int s_VertexOffset = Shader.PropertyToID("InfinityPreviousVertexOffset");
        static readonly int s_PreviousMatrix = Shader.PropertyToID("InfinityPreviousObjectToWorld");
        sealed class Binding
        {
            internal readonly MaterialPropertyBlock original = new MaterialPropertyBlock();
            internal readonly MaterialPropertyBlock bound = new MaterialPropertyBlock();
            internal int index;
        }
        sealed class Entry
        {
            internal Renderer renderer;
            internal Matrix4x4 committed, pending;
            internal bool valid, geometryCompatible;
            internal long committedView;
            internal Mesh bakeMesh, committedMesh, pendingMesh;
            internal List<Vector3> committedVertices = new List<Vector3>(), pendingVertices = new List<Vector3>();
            internal int vertexOffset = -1;
            internal readonly List<Binding> bindings = new List<Binding>();
            internal int boundCount;
        }
        readonly Dictionary<Renderer, Entry> m_Entries = new Dictionary<Renderer, Entry>();
        readonly List<Entry> m_Pending = new List<Entry>();
        readonly List<Renderer> m_Dead = new List<Renderer>();
        readonly List<Vector3> m_PreviousVertices = new List<Vector3>();
        Vector3[] m_VertexUpload = new Vector3[1];
        long m_CommittedView;
        internal Vector3[] PreviousVertices => m_VertexUpload;
        readonly List<Material> m_Materials = new List<Material>();

        internal void Prepare()
        {
            m_Pending.Clear(); m_Dead.Clear(); m_PreviousVertices.Clear();
            foreach (var pair in m_Entries) if (pair.Key == null) m_Dead.Add(pair.Key);
            foreach (var renderer in m_Dead) { UnityEngine.Rendering.CoreUtils.Destroy(m_Entries[renderer].bakeMesh); m_Entries.Remove(renderer); }
            foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (!renderer.gameObject.scene.IsValid() || !renderer.gameObject.scene.isLoaded
                    || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                renderer.GetSharedMaterials(m_Materials);
                bool supported = false;
                foreach (var material in m_Materials)
                    if (material != null && material.shader.name == "InfinityPipeline/InfinityLit") supported = true;
                if (!supported) continue;
                if (!m_Entries.TryGetValue(renderer, out Entry entry))
                { entry = new Entry { renderer = renderer }; m_Entries.Add(renderer, entry); }
                entry.pending = renderer.localToWorldMatrix; entry.boundCount = 0;
                entry.vertexOffset = -1;
                entry.geometryCompatible = entry.valid && entry.committedView == m_CommittedView;
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null
                    && renderer.motionVectorGenerationMode == MotionVectorGenerationMode.Object)
                {
                    if (entry.bakeMesh == null) entry.bakeMesh = new Mesh { name = "NativeMotionPose", hideFlags = HideFlags.DontSave };
                    // Keep the snapshot unscaled: the committed renderer matrix applies
                    // its transform scale exactly once during previous-position projection.
                    skin.BakeMesh(entry.bakeMesh, true);
                    entry.bakeMesh.GetVertices(entry.pendingVertices); entry.pendingMesh = skin.sharedMesh;
                    entry.geometryCompatible &= entry.committedMesh == entry.pendingMesh
                        && entry.committedVertices.Count == entry.pendingVertices.Count;
                    entry.vertexOffset = m_PreviousVertices.Count;
                    m_PreviousVertices.AddRange(entry.geometryCompatible ? entry.committedVertices : entry.pendingVertices);
                }
                else
                {
                    entry.pendingMesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    entry.pendingVertices.Clear();
                    entry.geometryCompatible &= entry.pendingMesh == entry.committedMesh;
                }
                m_Pending.Add(entry);
                Bind(entry, -1);
                // A material-index block overrides the renderer block completely.
                for (int i = 0; i < m_Materials.Count; ++i)
                {
                    var block = GetBinding(entry, entry.boundCount);
                    renderer.GetPropertyBlock(block.original, i);
                    if (!block.original.isEmpty) Bind(entry, i);
                }
            }
            int count = Math.Max(1, m_PreviousVertices.Count);
            if (m_VertexUpload.Length != count) m_VertexUpload = new Vector3[count];
            if (m_PreviousVertices.Count > 0) m_PreviousVertices.CopyTo(m_VertexUpload);
            else m_VertexUpload[0] = Vector3.zero;
        }
        static Binding GetBinding(Entry entry, int index)
        {
            if (entry.bindings.Count <= index) entry.bindings.Add(new Binding());
            return entry.bindings[index];
        }
        static void Bind(Entry entry, int index)
        {
            var block = GetBinding(entry, entry.boundCount++); block.index = index;
            if (index < 0) { entry.renderer.GetPropertyBlock(block.original); entry.renderer.GetPropertyBlock(block.bound); }
            else { entry.renderer.GetPropertyBlock(block.original, index); entry.renderer.GetPropertyBlock(block.bound, index); }
            var mode = entry.renderer.motionVectorGenerationMode;
            Matrix4x4 previous = mode == MotionVectorGenerationMode.ForceNoMotion || !entry.geometryCompatible ? Matrix4x4.zero
                : mode == MotionVectorGenerationMode.Camera ? entry.pending
                : entry.committed;
            block.bound.SetMatrix(s_PreviousMatrix, previous);
            block.bound.SetFloat(s_VertexOffset, entry.vertexOffset);
            if (index < 0) entry.renderer.SetPropertyBlock(block.bound);
            else entry.renderer.SetPropertyBlock(block.bound, index);
        }
        internal void RestoreBindings()
        {
            foreach (var entry in m_Pending)
            {
                if (entry.renderer == null) continue;
                for (int i = 0; i < entry.boundCount; ++i)
                {
                    var block = entry.bindings[i];
                    if (block.index < 0) entry.renderer.SetPropertyBlock(block.original.isEmpty ? null : block.original);
                    else entry.renderer.SetPropertyBlock(block.original.isEmpty ? null : block.original, block.index);
                }
                entry.boundCount = 0;
            }
        }
        internal void Commit()
        {
            ++m_CommittedView;
            foreach (var entry in m_Pending)
            {
                entry.committed = entry.pending; entry.valid = true; entry.committedMesh = entry.pendingMesh;
                entry.committedView = m_CommittedView;
                var vertices = entry.committedVertices; entry.committedVertices = entry.pendingVertices; entry.pendingVertices = vertices;
            }
            m_Pending.Clear();
        }
        internal void Rollback() { RestoreBindings(); m_Pending.Clear(); }
        internal void Clear() { Rollback(); foreach (var entry in m_Entries.Values) UnityEngine.Rendering.CoreUtils.Destroy(entry.bakeMesh); m_Entries.Clear(); }
    }
}
