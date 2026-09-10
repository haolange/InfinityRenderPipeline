using System;
using Unity.Mathematics;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    // Like the camera matrices, object poses are relative to this view's last accepted submission.
    internal sealed class MeshViewMotionHistory
    {
        float4x4[] m_Committed = Array.Empty<float4x4>();
        uint[] m_CommittedGenerations = Array.Empty<uint>();
        float4x4[] m_Pending = Array.Empty<float4x4>();
        uint[] m_PendingGenerations = Array.Empty<uint>();
        float4x4[] m_Previous = Array.Empty<float4x4>();
        int m_CommittedScene, m_PendingScene;
        bool m_Prepared;

        internal float4x4[] Prepare(MeshScene scene)
        {
            int count = Math.Max(1, scene.TransformHighWater);
            if (m_Pending.Length != count)
            {
                m_Pending = new float4x4[count]; m_PendingGenerations = new uint[count];
            }
            if (m_Previous.Length != count) m_Previous = new float4x4[count];
            var transforms = scene.GetTransforms(); var generations = scene.GetTransformGenerations();
            for (int i = 0; i < count; ++i)
            {
                bool exists = i < scene.TransformHighWater;
                uint generation = exists ? generations[i] : 0;
                m_Pending[i] = exists ? transforms[i].current : default;
                m_PendingGenerations[i] = generation;
                // A zero previous matrix produces invalid clip W: a new/reused slot has no history surface.
                m_Previous[i] = generation != 0 && m_CommittedScene == scene.SceneId && i < m_Committed.Length
                    && m_CommittedGenerations[i] == generation ? m_Committed[i] : default;
            }
            m_PendingScene = scene.SceneId; m_Prepared = true;
            return m_Previous;
        }

        internal void Commit()
        {
            if (!m_Prepared) return;
            var transforms = m_Committed; m_Committed = m_Pending; m_Pending = transforms;
            var generations = m_CommittedGenerations; m_CommittedGenerations = m_PendingGenerations; m_PendingGenerations = generations;
            m_CommittedScene = m_PendingScene; m_Prepared = false;
        }
        internal void Rollback() => m_Prepared = false;
        internal void Clear()
        {
            m_Committed = m_Pending = m_Previous = Array.Empty<float4x4>();
            m_CommittedGenerations = m_PendingGenerations = Array.Empty<uint>();
            m_Prepared = false; m_CommittedScene = m_PendingScene = 0;
        }
    }
}
