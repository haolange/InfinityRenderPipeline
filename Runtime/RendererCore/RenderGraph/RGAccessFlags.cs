using System;

namespace InfinityTech.Rendering.RenderGraph
{
    /// <summary>
    /// 定义了对颜色附件（Color Attachment）的访问意图。
    /// 用于在编译期自动推导Load/Store Action。
    /// </summary>
    public enum EColorAccessFlag
    {
        /// <summary>
        /// 写入附件，但不关心之前的像素值。
        /// 对应LoadAction=DontCare或Clear，会进行全屏写入。这是最高效的写入方式。
        /// </summary>
        WriteAll,

        /// <summary>
        /// 在保留现有像素值的基础上进行写入（例如混合操作）。
        /// 对应LoadAction=Load，RG会确保加载之前的颜色数据。
        /// </summary>
        Write,

        /// <summary>
        /// 写入附件，但明确告诉RG无需加载旧值（即使屏幕上只有部分区域被写入）。
        /// 开发者需要自行保证未写入区域不会被读取到未定义的值。对应LoadAction=DontCare。
        /// </summary>
        Discard,

        /// <summary>
        /// 作为输入附件（Input Attachment）进行读取。
        /// 主要用于移动端实现Tile-Based延迟渲染的On-Chip优化，若不满足合并条件则回退为普通Texture采样。
        /// </summary>
        Read
    }

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