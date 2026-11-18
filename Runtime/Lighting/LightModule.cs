using System;
using InfinityTech.Core;
using InfinityTech.Component;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.LightPipeline
{
    /// <summary>
    /// The light module of the scene world. Persistent layer: the light table (light entity id to its additional data,
    /// derived from the component registry), the cookie atlas and the cached local shadow atlas. View layer: the light
    /// context, rebuilt for each camera from that camera's culling results and valid until the next camera builds it. Shadow
    /// casters are the members of the pipeline's shadow caster pass, which the module receives at construction.
    /// </summary>
    internal sealed class LightModule : IDisposable
    {
        public readonly SceneTable<InfinityAdditionalLightData> table = new SceneTable<InfinityAdditionalLightData>();
        public readonly LightCookieAtlas cookieAtlas;
        public readonly LocalShadowCache shadowCache;
        public readonly LightContext context;
        // The mesh pass whose members cast shadows, in the scene world's pass table; LightContext.Build reads it next to the
        // mesh world.
        public readonly MeshPassId shadowCasterPass;

        public LightModule(ResourcePool resourcePool, MeshPassId shadowCasterPass)
        {
            this.shadowCasterPass = shadowCasterPass;
            cookieAtlas = new LightCookieAtlas(resourcePool);
            shadowCache = new LocalShadowCache(resourcePool);
            context = new LightContext();
        }

        public void Prepare() => table.Prepare(InfinityAdditionalLightData.s_Registry);

        public void Commit() => table.Commit();

        public void Rollback() => table.Rollback();

        public void Dispose()
        {
            context.Dispose();
            shadowCache.Dispose();
            cookieAtlas.Dispose();
        }
    }
}
