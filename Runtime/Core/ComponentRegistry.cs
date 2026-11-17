using System;
using System.Collections.Generic;

namespace InfinityTech.Core
{
    /// <summary>
    /// The live set of one component type: the single authority for "this component exists and is enabled". It lives as
    /// long as the process, like the Unity objects it mirrors, and holds no work for any world. A scene world derives its
    /// tables from it and rebuilds them from it when the world is created.
    /// </summary>
    public sealed class ComponentRegistry<T> where T : UnityEngine.Object
    {
        readonly Dictionary<ulong, T> m_Components = new Dictionary<ulong, T>(64);

        /// <summary>Advances on every registration and removal; a table compares it with the version it was built from.</summary>
        public int version { get; private set; }

        public int count => m_Components.Count;

        public Dictionary<ulong, T>.Enumerator GetEnumerator() => m_Components.GetEnumerator();

        public Dictionary<ulong, T>.ValueCollection components => m_Components.Values;

        public bool Contains(ulong id) => m_Components.ContainsKey(id);

        /// <summary>The component registers once per enable with its stable id; a second registration of the id is a defect.</summary>
        public void Register(ulong id, T component)
        {
            if (ReferenceEquals(component, null))
            {
                throw new ArgumentNullException(nameof(component));
            }
            if (!m_Components.TryAdd(id, component))
            {
                throw new InvalidOperationException($"{typeof(T).Name} {id} is already registered.");
            }
            ++version;
        }

        public void Unregister(ulong id)
        {
            if (!m_Components.Remove(id))
            {
                throw new InvalidOperationException($"{typeof(T).Name} {id} is not registered.");
            }
            ++version;
        }
    }
}
