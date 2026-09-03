using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;

namespace InfinityTech.Rendering.MeshPipeline
{
    public static class MeshPassShaderUtility
    {
        struct CacheKey : IEquatable<CacheKey>
        {
            public int materialInstanceId;
            public string lightMode;

            public bool Equals(CacheKey other)
            {
                return materialInstanceId == other.materialInstanceId
                    && string.Equals(lightMode, other.lightMode, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = materialInstanceId * 397;
                    if (lightMode != null)
                    {
                        hash ^= lightMode.GetHashCode();
                    }
                    return hash;
                }
            }
        }

        static readonly ShaderTagId s_LightModeTag = new ShaderTagId("LightMode");
        static readonly Dictionary<CacheKey, int> s_Cache = new Dictionary<CacheKey, int>(256);

        public static int FindPassIndex(Material material, string lightMode)
        {
            if (material == null || string.IsNullOrEmpty(lightMode))
            {
                return -1;
            }

            CacheKey key = new CacheKey
            {
                materialInstanceId = UnityEntityId.ToInt32(material),
                lightMode = lightMode
            };
            if (s_Cache.TryGetValue(key, out int cached))
            {
                return cached;
            }

            int passIndex = material.FindPass(lightMode);
            if (passIndex < 0)
            {
                Shader shader = material.shader;
                if (shader != null)
                {
                    ShaderTagId wanted = new ShaderTagId(lightMode);
                    int passCount = shader.passCount;
                    for (int i = 0; i < passCount; ++i)
                    {
                        if (shader.FindPassTagValue(i, s_LightModeTag) == wanted)
                        {
                            passIndex = i;
                            break;
                        }
                    }
                }
            }

            s_Cache[key] = passIndex;
            return passIndex;
        }

        public static int ResolvePassIndex(Material material, string lightMode, int fallbackPassIndex)
        {
            if (string.IsNullOrEmpty(lightMode))
            {
                return fallbackPassIndex;
            }

            return FindPassIndex(material, lightMode);
        }
    }
}
