using InfinityTech.Core;
using InfinityTech.Component;

namespace InfinityTech.Rendering.Scene
{
    /// <summary>
    /// The decal module of the scene world. Persistent layer: the decal table (component entity id to component, derived
    /// from the component registry). Decals draw through Unity renderers in the DBuffer pass; the table decides whether
    /// the camera records that pass.
    /// </summary>
    internal sealed class DecalModule
    {
        public readonly SceneTable<DecalComponent> table = new SceneTable<DecalComponent>();

        public void Prepare() => table.Prepare(DecalComponent.s_Registry);

        public void Commit() => table.Commit();

        public void Rollback() => table.Rollback();
    }
}
