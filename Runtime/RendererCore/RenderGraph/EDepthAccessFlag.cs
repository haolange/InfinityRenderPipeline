namespace InfinityTech.Rendering.RenderGraph
{
    /// <summary>
    /// 定义了对深度/模板附件（Depth/Stencil Attachment）的访问意图。
    /// </summary>
    public enum EDepthAccessFlag
    {
        /// <summary>
        /// 只读。仅用于深度测试，不写入任何深度值。
        /// </summary>
        ReadOnly,

        /// <summary>
        /// 读写。标准的深度测试与写入。
        /// </summary>
        ReadWrite
    }
}