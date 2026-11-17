using System;
using InfinityTech.Core;
using InfinityTech.Component;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.LightPipeline
{
    /// <summary>
    /// The reflection-probe module of the scene world. Persistent layer: the probe table (ReflectionProbe entity id to its
    /// additional data, derived from the component registry) and the prefiltered probe atlas. Its view layer is the probe
    /// list the light context resolves for each camera.
    /// </summary>
    internal sealed class ProbeModule : IDisposable
    {
        public readonly SceneTable<InfinityAdditionalReflectionProbeData> table = new SceneTable<InfinityAdditionalReflectionProbeData>();
        public readonly ReflectionProbeAtlas atlas;

        public ProbeModule(ResourcePool resourcePool)
        {
            atlas = new ReflectionProbeAtlas(resourcePool);
        }

        public void Prepare() => table.Prepare(InfinityAdditionalReflectionProbeData.s_Registry);

        public void Commit() => table.Commit();

        public void Rollback() => table.Rollback();

        public void Dispose() => atlas.Dispose();
    }
}
