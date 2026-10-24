using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    // Physical allocation state owned by ResourcePool, independent of logical graph handles.
    internal sealed class ResourceUsage
    {
        internal bool returned;
        internal UnityEngine.RenderTexture textureIdentity;
        internal bool uncertain => m_Uncertain;
        int m_RecordReferences, m_Queues;
        readonly HashSet<int> m_PendingEpochs = new HashSet<int>();
        internal bool pending => m_Pending;
        bool m_Pending, m_Uncertain, m_HasSubmittedUse, m_HasCompletion, m_Completed;
        GraphicsFence m_Completion;
        ResourceReuseFence m_Tail, m_SubmittedTail, m_ReturnFence;

        internal void RetainRecording()
        {
            if (returned) throw new InvalidOperationException("A returned allocation cannot be recorded without acquiring it again.");
            m_RecordReferences++;
        }
        internal void ReleaseRecording()
        {
            if (--m_RecordReferences < 0) throw new InvalidOperationException("Resource recording reference released twice.");
        }
        internal bool CanWriteCpu
        {
            get
            {
                if (m_RecordReferences != 0 || m_Pending || m_Uncertain) return false;
                if (!m_ReturnFence.passed) return false;
                if (!m_HasSubmittedUse || m_Completed) return true;
                if (!m_HasCompletion || !m_Completion.passed) return false;
                m_Completed = true;
                return true;
            }
        }
        internal bool CanAcquireCommands(bool asyncCompute, int epoch, out ResourceReuseFence wait)
        {
            wait = default;
            if (m_RecordReferences != 0 || m_Uncertain || m_Pending && (m_PendingEpochs.Count != 1 || !m_PendingEpochs.Contains(epoch))) return false;
            if (CanWriteCpu) return true;
            wait = m_Pending ? m_Tail : m_SubmittedTail;
            // Same-queue command order is sufficient. An opposite queue needs an actually
            // queued fence; a future frame/completion signal is not a substitute.
            return asyncCompute ? (m_Queues & 1) == 0 || wait.hasGraphics : (m_Queues & 2) == 0 || wait.hasCompute;
        }
        internal void QueueAttempt(int epoch, bool asyncCompute, ResourceReuseFence fence)
        {
            if (!m_Pending)
            {
                m_Queues = 0; m_Tail = default;
            }
            else if (!m_PendingEpochs.Contains(epoch)) m_Uncertain = true;
            m_PendingEpochs.Add(epoch);
            m_Pending = true; m_Completed = false;
            m_Queues |= asyncCompute ? 2 : 1;
            if (asyncCompute)
            {
                m_Tail.hasCompute = fence.hasCompute;
                m_Tail.compute = fence.compute; m_Tail.computeCompletion = fence.computeCompletion;
            }
            else
            {
                m_Tail.hasGraphics = fence.hasGraphics;
                m_Tail.graphics = fence.graphics; m_Tail.graphicsCompletion = fence.graphicsCompletion;
            }
        }
        internal void SetTail(ResourceReuseFence fence)
        {
            if (fence.hasGraphics)
            {
                m_Queues |= 1;
                m_Tail.hasGraphics = m_ReturnFence.hasGraphics = true;
                m_Tail.graphics = m_ReturnFence.graphics = fence.graphics;
                m_Tail.graphicsCompletion = m_ReturnFence.graphicsCompletion = fence.graphicsCompletion;
                if (!m_Pending)
                {
                    m_SubmittedTail.hasGraphics = true; m_SubmittedTail.graphics = fence.graphics;
                    m_SubmittedTail.graphicsCompletion = fence.graphicsCompletion;
                }
            }
            if (fence.hasCompute)
            {
                m_Queues |= 2;
                m_Tail.hasCompute = m_ReturnFence.hasCompute = true;
                m_Tail.compute = m_ReturnFence.compute = fence.compute;
                m_Tail.computeCompletion = m_ReturnFence.computeCompletion = fence.computeCompletion;
                if (!m_Pending)
                {
                    m_SubmittedTail.hasCompute = true; m_SubmittedTail.compute = fence.compute;
                    m_SubmittedTail.computeCompletion = fence.computeCompletion;
                }
            }
        }
        internal void MarkUncertain() => m_Uncertain = true;
        internal void Submitted(int epoch, GraphicsFence completion, bool hasCompletion, ResourceReuseFence joinedTail)
        {
            if (!m_PendingEpochs.Remove(epoch)) return;
            if (m_PendingEpochs.Count != 0)
            {
                // Another context/batch may still hold unsubmitted commands. This completion
                // signal cannot acknowledge those commands, even on the same physical queue.
                m_Uncertain = true;
                return;
            }
            m_Pending = m_Uncertain = false;
            m_HasSubmittedUse = true; m_HasCompletion = hasCompletion;
            m_Completion = completion; m_Completed = false;
            m_SubmittedTail = joinedTail.hasGraphics || joinedTail.hasCompute ? joinedTail : m_Tail;
            if (joinedTail.hasGraphics) m_Queues = 1; // The frame graphics fence follows the async join.
        }
    }
}
