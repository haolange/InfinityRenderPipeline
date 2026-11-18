namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// A pass of the table its world was built with: the index of its <see cref="MeshPassDesc"/>. The pipeline that owns the
    /// table names its passes by these ids; the mesh mechanism never names a pass.
    /// </summary>
    internal readonly struct MeshPassId
    {
        internal readonly int index;

        internal MeshPassId(int index) => this.index = index;

        public override string ToString() => index.ToString();
    }
}
