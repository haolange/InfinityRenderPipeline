using System;
using System.Collections.Generic;

namespace InfinityTech.Core
{
    /// <summary>
    /// One scene module's persistent table: id to component, derived from that component type's registry inside the scene
    /// transaction. Readers see the committed table only. <see cref="Prepare"/> stages a rebuild when the registry changed,
    /// <see cref="Commit"/> publishes it and advances <see cref="revision"/>, and <see cref="Rollback"/> drops it.
    /// </summary>
    public sealed class SceneTable<T> where T : UnityEngine.Object
    {
        Dictionary<ulong, T> m_Committed = new Dictionary<ulong, T>(64);
        Dictionary<ulong, T> m_Staged = new Dictionary<ulong, T>(64);
        int m_CommittedVersion = -1;
        int m_StagedVersion;
        bool m_HasStaged;
        bool m_Preparing;

        /// <summary>Advances once per published change of membership.</summary>
        public int revision { get; private set; }

        public int count => m_Committed.Count;

        public bool TryGet(ulong id, out T component) => m_Committed.TryGetValue(id, out component);

        public Dictionary<ulong, T>.ValueCollection components => m_Committed.Values;

        /// <summary>Stages the registry's membership when it changed since the committed table was built.</summary>
        public void Prepare(ComponentRegistry<T> registry)
        {
            if (m_Preparing)
            {
                throw new InvalidOperationException("A scene table transaction is already open.");
            }
            m_Preparing = true;
            m_HasStaged = registry.version != m_CommittedVersion;
            if (!m_HasStaged)
            {
                return;
            }
            m_Staged.Clear();
            foreach (KeyValuePair<ulong, T> pair in registry)
            {
                m_Staged.Add(pair.Key, pair.Value);
            }
            m_StagedVersion = registry.version;
        }

        public void Commit()
        {
            if (!m_Preparing)
            {
                throw new InvalidOperationException("Commit needs a prepared scene table.");
            }
            m_Preparing = false;
            if (!m_HasStaged)
            {
                return;
            }
            (m_Committed, m_Staged) = (m_Staged, m_Committed);
            m_CommittedVersion = m_StagedVersion;
            m_HasStaged = false;
            ++revision;
        }

        public void Rollback()
        {
            m_Preparing = false;
            m_HasStaged = false;
        }
    }
}
