using UnityEngine.Rendering;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// One pass of a world's table: LightMode, queues, entity flags, sort key and previous transforms. Its identity is its
    /// <see cref="MeshPassId"/> in that table.
    /// </summary>
    internal readonly struct MeshPassDesc
    {
        public readonly string lightMode;
        /// <summary>The short name of the pass in profiler markers (Depth, GBuffer, Shadow, ...).</summary>
        public readonly string token;
        public readonly int queueMin;
        public readonly int queueMax;
        public readonly EMeshEntityFlags requireEntityFlags;
        /// <summary>The draw list binds its view's accepted previous transforms and fails at record without them.</summary>
        public readonly bool requirePreviousTransforms;
        /// <summary>Which compiled gather writes the instance key; it also decides the backend (MeshWorld.SelectBackend).</summary>
        public readonly EMeshSortKey sortKey;

        public MeshPassDesc(string lightMode, string token, int queueMin, int queueMax,
            EMeshEntityFlags requireEntityFlags = EMeshEntityFlags.None, bool requirePreviousTransforms = false,
            EMeshSortKey sortKey = EMeshSortKey.State)
        {
            this.lightMode = lightMode;
            this.token = token;
            this.queueMin = queueMin;
            this.queueMax = queueMax;
            this.requireEntityFlags = requireEntityFlags;
            this.requirePreviousTransforms = requirePreviousTransforms;
            this.sortKey = sortKey;
        }

        /// <summary>Opaque passes cross-fade LOD transitions with dithering; translucent passes draw the stable level only.</summary>
        public bool ditherLODTransitions => queueMax < (int)RenderQueue.Transparent;

        public override string ToString() => $"{lightMode} [{queueMin}, {queueMax}]";
    }
}
