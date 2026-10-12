using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfinityTech.Rendering.RenderGraph
{
    // Semantic identity of a per-frame graph resource registered in RGScoper (for example "_DepthTexture").
    // The id equals Shader.PropertyToID(name), so a key converts implicitly to int wherever a shader property ID
    // or a scoper handle is expected. The name is kept so missing or conflicting registrations report what they
    // were looking for.
    public readonly struct RGResourceKey : IEquatable<RGResourceKey>
    {
        static readonly Dictionary<int, string> s_Names = new Dictionary<int, string>(128);

        public readonly int id;
        public readonly string name;

        public RGResourceKey(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A graph resource key needs a name.", nameof(name));
            this.name = name;
            id = Shader.PropertyToID(name);
            lock (s_Names)
            {
                s_Names[id] = name;
            }
        }

        public static implicit operator int(RGResourceKey key) => key.id;

        // Name of a registered key, or the numeric id when the handle was never declared as a key.
        public static string NameOf(int id)
        {
            lock (s_Names)
            {
                return s_Names.TryGetValue(id, out string name) ? name : id.ToString();
            }
        }

        public bool Equals(RGResourceKey other) => id == other.id;
        public override bool Equals(object obj) => obj is RGResourceKey other && Equals(other);
        public override int GetHashCode() => id;
        public override string ToString() => name ?? id.ToString();

        public static bool operator ==(RGResourceKey a, RGResourceKey b) => a.id == b.id;
        public static bool operator !=(RGResourceKey a, RGResourceKey b) => a.id != b.id;
    }
}
