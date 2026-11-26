using System;
using InfinityTech.Core;
using InfinityTech.Component;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Reflection
{
    /// <summary>
    /// The reflection module of the scene world: probe extension table, raster reflection volume table, prefiltered probe
    /// atlas, and per-camera clustered <see cref="ReflectionContext"/>.
    /// </summary>
    internal sealed class ProbeModule : IDisposable
    {
        public readonly SceneTable<InfinityAdditionalReflectionProbeData> table = new SceneTable<InfinityAdditionalReflectionProbeData>();
        public readonly SceneTable<InfinityRasterReflectionVolume> rasterTable = new SceneTable<InfinityRasterReflectionVolume>();
        public readonly ReflectionProbeAtlas atlas;
        public readonly ReflectionContext context;

        public ProbeModule(ResourcePool resourcePool)
        {
            atlas = new ReflectionProbeAtlas(resourcePool);
            context = new ReflectionContext();
        }

        public void Prepare()
        {
            table.Prepare(InfinityAdditionalReflectionProbeData.s_Registry);
            rasterTable.Prepare(InfinityRasterReflectionVolume.s_Registry);
        }

        public void Commit()
        {
            table.Commit();
            rasterTable.Commit();
        }

        public void Rollback()
        {
            table.Rollback();
            rasterTable.Rollback();
        }

        public void Dispose()
        {
            context.Dispose();
            atlas.Dispose();
        }
    }
}
