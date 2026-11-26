using System;
using InfinityTech.Core;
using InfinityTech.Rendering.AnalyticShadow;

namespace InfinityTech.Rendering.SceneModules
{
    /// <summary>
    /// Persistent analytic occluder table and per-camera spatial acceleration context on <see cref="SceneWorld"/>.
    /// </summary>
    internal sealed class AnalyticShadowModule : IDisposable
    {
        public readonly SceneTable<InfinityAnalyticOccluder> table = new SceneTable<InfinityAnalyticOccluder>();
        public readonly AnalyticShadowContext context = new AnalyticShadowContext();

        public void Prepare() => table.Prepare(InfinityAnalyticOccluder.s_Registry);

        public void Commit() => table.Commit();

        public void Rollback() => table.Rollback();

        public void Dispose() => context.Dispose();
    }
}
