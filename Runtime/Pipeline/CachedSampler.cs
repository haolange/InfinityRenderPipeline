using System.Collections.Generic;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering
{
    /// <summary>One ProfilingSampler per marker name. The first use of a name creates it; later uses do not allocate.</summary>
    internal static class CachedSampler
    {
        static readonly Dictionary<string, ProfilingSampler> s_Samplers = new Dictionary<string, ProfilingSampler>(64);

        public static ProfilingSampler Get(string name)
        {
            if (!s_Samplers.TryGetValue(name, out ProfilingSampler sampler))
            {
                sampler = new ProfilingSampler(name);
                s_Samplers.Add(name, sampler);
            }
            return sampler;
        }
    }
}
