using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    /// <summary>
    /// 演示Subpass合并的示例Pass
    /// 这个Pass展示了如何使用SetInputAttachment来实现像素级数据传递，
    /// 从而触发RG编译器的Subpass合并优化
    /// </summary>
    public partial class InfinityRenderPipeline
    {
        struct SubpassDemoPassData
        {
            // 这里可以添加Pass需要的数据
        }

        /// <summary>
        /// 演示如何使用新的API实现Subpass优化的示例
        /// </summary>
        void DemonstrateSubpassMerging(RenderContext renderContext, Camera camera)
        {
            // 假设我们有一个GBuffer Pass的输出
            RGTextureRef gbufferAlbedo = m_RGScoper.QueryTexture("GBufferAlbedo");
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            
            // 创建输出纹理
            TextureDescriptor lightingTextureDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            lightingTextureDsc.name = "SubpassDemo_Output";
            lightingTextureDsc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.B10G11R11_UFloatPack32;
            RGTextureRef outputTexture = m_RGScoper.CreateAndRegisterTexture("SubpassDemo", lightingTextureDsc);

            // Pass 1: 一个常规的写入Pass（例如GBuffer写入）
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<SubpassDemoPassData>(ProfilingSampler.Get(CustomSamplerId.RenderGBuffer)))
            {
                passRef.SetColorAttachment(gbufferAlbedo, 0, EAccessFlag.WriteAll);
                passRef.SetDepthAttachment(depthTexture, EAccessFlag.WriteAll);
                passRef.AllowPassMerge(true);  // 允许Pass合并

                passRef.SetExecuteFunc((in SubpassDemoPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    // GBuffer写入逻辑
                });
            }

            // Pass 2: 一个使用输入附件的Pass（展示Subpass合并）
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<SubpassDemoPassData>(ProfilingSampler.Get(CustomSamplerId.RenderForward)))
            {
                // 关键：使用SetInputAttachment声明我们要在同一像素位置读取前一个Pass的输出
                passRef.SetInputAttachment(gbufferAlbedo, EAccessFlag.Read);
                
                // 输出到不同的附件
                passRef.SetColorAttachment(outputTexture, 0, EAccessFlag.WriteAll);
                passRef.SetDepthAttachment(depthTexture, EAccessFlag.Read);  // 深度测试，不写入
                passRef.AllowPassMerge(true);  // 允许Pass合并

                passRef.SetExecuteFunc((in SubpassDemoPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    // 在Shader中，可以使用以下方式读取输入附件：
                    // - Vulkan: subpassLoad()
                    // - Metal: 片段函数输入参数
                    // - D3D12: Pixel Shader中的SV_Target输入
                    
                    // 这里的绘制命令会和前一个Pass合并到同一个RenderPass中作为Subpass 1执行
                });
            }
        }
    }
}