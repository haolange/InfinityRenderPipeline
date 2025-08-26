using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Examples
{
    /// <summary>
    /// 展示如何使用新的RenderGraph访问标志系统的示例代码。
    /// 这个示例演示了如何从旧的LoadAction/StoreAction API迁移到新的访问标志API。
    /// </summary>
    public static class RenderGraphNewAPIExample
    {
        struct ExamplePassData
        {
            public RGTextureRef colorTarget;
            public RGTextureRef depthTarget;
            public RGTextureRef inputTexture;
        }

        /// <summary>
        /// 旧的API使用方式（已废弃）
        /// </summary>
        public static void OldAPIExample(RGBuilder builder)
        {
            // 旧方式：需要手动指定LoadAction和StoreAction
            using (var passRef = builder.AddRasterPass<ExamplePassData>(new ProfilingSampler("OldExample")))
            {
                var passData = passRef.GetPassData<ExamplePassData>();
                
                // 旧的API - 会显示废弃警告
                #pragma warning disable CS0618 // Type or member is obsolete
                passData.colorTarget = passRef.SetColorAttachment(passData.colorTarget, 0, 
                    RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passData.depthTarget = passRef.SetDepthStencilAttachment(passData.depthTarget,
                    RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);
                #pragma warning restore CS0618

                passRef.SetExecuteFunc<ExamplePassData>((in ExamplePassData data, in RGRasterEncoder encoder, RGObjectPool pool) =>
                {
                    // 执行渲染逻辑
                });
            }
        }

        /// <summary>
        /// 新的API使用方式（推荐）
        /// </summary>
        public static void NewAPIExample(RGBuilder builder)
        {
            // 新方式：使用访问标志，系统自动推导LoadAction和StoreAction
            using (var passRef = builder.AddRasterPass<ExamplePassData>(new ProfilingSampler("NewExample")))
            {
                var passData = passRef.GetPassData<ExamplePassData>();
                
                // 新的API - 使用访问标志
                // WriteAll: 全屏写入，不关心之前的内容，系统会选择Clear或DontCare
                passData.colorTarget = passRef.SetColorAttachment(passData.colorTarget, 0, EColorAccessFlag.WriteAll);
                
                // ReadWrite: 标准的深度测试与写入
                passData.depthTarget = passRef.SetDepthStencilAttachment(passData.depthTarget, EDepthAccessFlag.ReadWrite);

                passRef.SetExecuteFunc<ExamplePassData>((in ExamplePassData data, in RGRasterEncoder encoder, RGObjectPool pool) =>
                {
                    // 执行渲染逻辑
                });
            }
        }

        /// <summary>
        /// 演示不同访问标志的使用场景
        /// </summary>
        public static void AccessFlagExamples(RGBuilder builder)
        {
            // 示例1：延迟渲染的GBuffer Pass
            using (var gbufferPass = builder.AddRasterPass<ExamplePassData>(new ProfilingSampler("GBufferPass")))
            {
                var passData = gbufferPass.GetPassData<ExamplePassData>();
                
                // WriteAll: 完全重写GBuffer，不需要之前的内容
                passData.colorTarget = gbufferPass.SetColorAttachment(passData.colorTarget, 0, EColorAccessFlag.WriteAll);
                passData.depthTarget = gbufferPass.SetDepthStencilAttachment(passData.depthTarget, EDepthAccessFlag.ReadWrite);
                
                gbufferPass.SetExecuteFunc<ExamplePassData>((in ExamplePassData data, in RGRasterEncoder encoder, RGObjectPool pool) =>
                {
                    // 渲染几何体到GBuffer
                });
            }

            // 示例2：透明物体渲染Pass  
            using (var transparentPass = builder.AddRasterPass<ExamplePassData>(new ProfilingSampler("TransparentPass")))
            {
                var passData = transparentPass.GetPassData<ExamplePassData>();
                
                // Write: 需要与之前的颜色进行混合，必须加载之前的内容
                passData.colorTarget = transparentPass.SetColorAttachment(passData.colorTarget, 0, EColorAccessFlag.Write);
                
                // ReadOnly: 只读深度测试，不写入深度值
                passData.depthTarget = transparentPass.SetDepthStencilAttachment(passData.depthTarget, EDepthAccessFlag.ReadOnly);
                
                transparentPass.SetExecuteFunc<ExamplePassData>((in ExamplePassData data, in RGRasterEncoder encoder, RGObjectPool pool) =>
                {
                    // 渲染透明物体
                });
            }

            // 示例3：后处理Pass
            using (var postprocessPass = builder.AddRasterPass<ExamplePassData>(new ProfilingSampler("PostprocessPass")))
            {
                var passData = postprocessPass.GetPassData<ExamplePassData>();
                
                // 读取输入纹理
                passData.inputTexture = postprocessPass.ReadTexture(passData.inputTexture);
                
                // Discard: 全屏后处理，明确告诉系统不需要加载旧值
                passData.colorTarget = postprocessPass.SetColorAttachment(passData.colorTarget, 0, EColorAccessFlag.Discard);
                
                postprocessPass.SetExecuteFunc<ExamplePassData>((in ExamplePassData data, in RGRasterEncoder encoder, RGObjectPool pool) =>
                {
                    // 执行全屏后处理
                });
            }

            // 示例4：移动端延迟渲染的Lighting Pass（使用Input Attachment优化）
            using (var lightingPass = builder.AddRasterPass<ExamplePassData>(new ProfilingSampler("LightingPass")))
            {
                var passData = lightingPass.GetPassData<ExamplePassData>();
                
                // 使用Input Attachment读取GBuffer，利用移动端Tile-Based优化
                passData.inputTexture = lightingPass.SetInputAttachment(passData.inputTexture, 0);
                
                // WriteAll: 完全重写最终颜色
                passData.colorTarget = lightingPass.SetColorAttachment(passData.colorTarget, 0, EColorAccessFlag.WriteAll);
                
                lightingPass.SetExecuteFunc<ExamplePassData>((in ExamplePassData data, in RGRasterEncoder encoder, RGObjectPool pool) =>
                {
                    // 延迟光照计算
                });
            }
        }

        /// <summary>
        /// 演示Pass合并控制
        /// </summary>
        public static void PassMergingExample(RGBuilder builder)
        {
            // Pass1: 允许合并（默认行为）
            using (var pass1 = builder.AddRasterPass<ExamplePassData>(new ProfilingSampler("MergeablePass1")))
            {
                var passData = pass1.GetPassData<ExamplePassData>();
                passData.colorTarget = pass1.SetColorAttachment(passData.colorTarget, 0, EColorAccessFlag.WriteAll);
                // 默认 allowPassMerge = true，这个Pass可以与其他Pass合并
                
                pass1.SetExecuteFunc<ExamplePassData>((in ExamplePassData data, in RGRasterEncoder encoder, RGObjectPool pool) =>
                {
                    // 第一个Pass的渲染逻辑
                });
            }

            // Pass2: 允许合并且使用相同的渲染目标
            using (var pass2 = builder.AddRasterPass<ExamplePassData>(new ProfilingSampler("MergeablePass2")))
            {
                var passData = pass2.GetPassData<ExamplePassData>();
                passData.colorTarget = pass2.SetColorAttachment(passData.colorTarget, 0, EColorAccessFlag.Write);
                // 如果与Pass1使用相同的渲染目标，这两个Pass会被自动合并
                
                pass2.SetExecuteFunc<ExamplePassData>((in ExamplePassData data, in RGRasterEncoder encoder, RGObjectPool pool) =>
                {
                    // 第二个Pass的渲染逻辑，会在同一个NativeRenderPass中执行
                });
            }

            // Pass3: 禁止合并
            using (var pass3 = builder.AddRasterPass<ExamplePassData>(new ProfilingSampler("NonMergeablePass")))
            {
                var passData = pass3.GetPassData<ExamplePassData>();
                passData.colorTarget = pass3.SetColorAttachment(passData.colorTarget, 0, EColorAccessFlag.Write);
                
                // 明确禁止这个Pass参与合并
                pass3.AllowPassMerge(false);
                
                pass3.SetExecuteFunc<ExamplePassData>((in ExamplePassData data, in RGRasterEncoder encoder, RGObjectPool pool) =>
                {
                    // 这个Pass会独立执行，不会与其他Pass合并
                });
            }
        }

        /// <summary>
        /// 演示异步计算Pass的正确用法
        /// </summary>
        public static void AsyncComputeExample(RGBuilder builder)
        {
            using (var computePass = builder.AddComputePass<ExamplePassData>(new ProfilingSampler("AsyncComputePass")))
            {
                var passData = computePass.GetPassData<ExamplePassData>();
                
                // 启用异步计算
                computePass.EnableAsyncCompute(true);
                
                // 即使输出纹理没有被后续Pass读取，也强制保留这个Pass
                // 修复后：这个Pass不会被错误地裁剪掉
                computePass.EnablePassCulling(false);
                
                // 写入一个可能没有后续读取者的纹理
                passData.colorTarget = computePass.WriteTexture(passData.colorTarget);
                
                computePass.SetExecuteFunc<ExamplePassData>((in ExamplePassData data, in RGComputeEncoder encoder, RGObjectPool pool) =>
                {
                    // 异步计算逻辑，例如生成纹理用于后续帧使用
                });
            }
        }
    }
}