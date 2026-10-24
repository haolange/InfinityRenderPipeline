using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    // A view stores references, never a second matrix table. Sparse views retain
    // their own accepted scene version until their next successful submission.
    internal sealed class MeshViewMotionHistory
    {
        MeshTransformVersion m_Accepted, m_Pending;
        internal MeshTransformVersion accepted => m_Accepted;
        internal MeshMotionBinding pendingBinding => m_Pending != null ? new MeshMotionBinding(m_Pending, m_Accepted)
            : throw new System.InvalidOperationException("Mesh history must be prepared before recording view consumers.");

        internal MeshMotionBinding Prepare(MeshTransformVersion current)
        {
            if (current == null) throw new System.InvalidOperationException("Mesh residency must prepare the scene transform version before recording motion.");
            Rollback();
            m_Pending = current; current.Retain();
            return new MeshMotionBinding(current, m_Accepted);
        }
        internal void Commit()
        {
            if (m_Pending == null) return;
            if (!m_Pending.queued) { Rollback(); return; }
            m_Accepted?.Release(); m_Accepted = m_Pending; m_Pending = null;
        }
        internal void Rollback() { m_Pending?.Release(); m_Pending = null; }
        internal void Clear()
        {
            Rollback(); m_Accepted?.Release(); m_Accepted = null;
        }
    }
}
