using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    // The scene transform version a camera's last submitted frame accepted, by reference, never a second matrix
    // table. A camera that renders sparsely keeps its accepted version until its next successful submission.
    internal sealed class MeshMotionHistory
    {
        MeshTransformVersion m_Accepted, m_Pending;
        internal MeshTransformVersion accepted => m_Accepted;

        internal MeshMotionBinding Prepare(MeshTransformVersion current)
        {
            if (current == null)
            {
                throw new System.InvalidOperationException("Mesh residency must prepare the scene transform version before recording motion.");
            }
            Rollback();
            m_Pending = current;
            current.Retain();
            return new MeshMotionBinding(current, m_Accepted);
        }
        internal void Commit()
        {
            if (m_Pending == null)
            {
                return;
            }
            if (!m_Pending.queued)
            {
                Rollback();
                return;
            }
            m_Accepted?.Release();
            m_Accepted = m_Pending;
            m_Pending = null;
        }
        internal void Rollback()
        {
            m_Pending?.Release();
            m_Pending = null;
        }
        internal void Clear()
        {
            Rollback();
            m_Accepted?.Release();
            m_Accepted = null;
        }
    }
}
