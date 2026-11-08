using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.MeshPipeline
{
    public static class MeshPassShaderUtility
    {
        struct CacheKey : IEquatable<CacheKey>
        {
            public Shader shader;
            public string lightMode;

            public bool Equals(CacheKey other) => ReferenceEquals(shader, other.shader) && string.Equals(lightMode, other.lightMode, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode() => unchecked(shader.GetHashCode() * 397 ^ StringComparer.Ordinal.GetHashCode(lightMode));
        }

        static readonly ShaderTagId s_LightModeTag = new ShaderTagId("LightMode");
        static readonly ShaderTagId s_MeshInstancingTag = new ShaderTagId("InfinityMeshInstancing");
        static readonly ShaderTagId s_True = new ShaderTagId("True");
        static readonly Dictionary<CacheKey, int> s_Cache = new Dictionary<CacheKey, int>(256);

        /// <summary>Advances when shaders are imported; pass caches re-resolve their pass indices before the next record.</summary>
        public static int shaderEpoch { get; private set; }

        public static void NotifyShadersChanged()
        {
            s_Cache.Clear();
            unchecked { shaderEpoch++; }
        }

        /// <summary>The shader reads entity data through the mesh instancing interface (SubShader tag InfinityMeshInstancing = True).</summary>
        public static bool SupportsMeshInstancing(Shader shader) => shader != null && shader.FindSubshaderTagValue(0, s_MeshInstancingTag) == s_True;

        public static int FindPassIndex(Material material, string lightMode)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (string.IsNullOrEmpty(lightMode)) throw new ArgumentException("A LightMode is required.", nameof(lightMode));
            Shader shader = material.shader;
            var key = new CacheKey { shader = shader, lightMode = lightMode };
            if (s_Cache.TryGetValue(key, out int cached)) return cached;

            int passIndex = -1;
            var wanted = new ShaderTagId(lightMode);
            for (int i = 0; i < shader.passCount; ++i)
            {
                if (shader.FindPassTagValue(i, s_LightModeTag) == wanted)
                {
                    passIndex = i;
                    break;
                }
            }
            s_Cache[key] = passIndex;
            return passIndex;
        }
    }
}
