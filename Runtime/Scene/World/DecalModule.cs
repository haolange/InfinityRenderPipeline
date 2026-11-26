using System;
using InfinityTech.Core;
using InfinityTech.Component;
using InfinityTech.Rendering.Decal;

namespace InfinityTech.Rendering.SceneModules
{
    /// <summary>
    /// The decal module of the scene world: persistent table and per-camera <see cref="DecalContext"/> covering DBuffer,
    /// Direct GBuffer, and Clustered decals.
    /// </summary>
    internal sealed class DecalModule : IDisposable
    {
        public readonly SceneTable<DecalComponent> table = new SceneTable<DecalComponent>();
        public readonly DecalContext context = new DecalContext();

        public void Prepare() => table.Prepare(DecalComponent.s_Registry);

        public void Commit() => table.Commit();

        public void Rollback() => table.Rollback();

        public void Dispose() => context.Dispose();
    }
}
