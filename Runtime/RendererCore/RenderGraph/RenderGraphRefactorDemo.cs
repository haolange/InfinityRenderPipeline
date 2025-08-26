/*
 * RenderGraph 重构功能测试和演示
 * 
 * 本文件展示了新重构的RenderGraph系统的三大核心功能：
 * 1. 异步计算Pass裁剪Bug修复
 * 2. Load/Store Action自动推导
 * 3. Pass合并优化
 * 
 * 作者：RenderGraph重构团队
 */

using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    /// <summary>
    /// RenderGraph重构功能测试演示类
    /// 展示新功能的使用方法和效果
    /// </summary>
    public class RenderGraphRefactorDemo
    {
        /// <summary>
        /// 演示异步计算Pass裁剪Bug修复
        /// 
        /// 修复前：如果异步计算Pass写入的资源没有被后续Pass读取，
        /// 即使EnablePassCulling(false)，Pass仍会被错误裁剪导致执行错误
        /// 
        /// 修复后：强制保留的异步计算Pass会被正确处理，不会被裁剪
        /// </summary>
        public static void DemoAsyncComputeCullingFix(RGBuilder builder)
        {
            // 创建一个只有异步计算Pass写入的纹理
            var computeOutput = builder.CreateTexture(new TextureDescriptor(256, 256)
            {
                name = "AsyncComputeOutput",
                colorFormat = GraphicsFormat.R32G32B32A32_SFloat,
                enableRandomWrite = true
            });

            using (var computePass = builder.AddComputePass<EmptyPassData>(ProfilingSampler.Get("AsyncComputeDemo")))
            {
                // 关键：启用异步计算并强制保留Pass
                computePass.EnableAsyncCompute(true);
                computePass.EnablePassCulling(false);
                
                // 写入纹理但没有后续Pass读取
                computePass.WriteTexture(computeOutput);
                
                computePass.SetExecuteFunc((in EmptyPassData data, in RGComputeEncoder cmd, RGObjectPool pool) =>
                {
                    // 异步计算逻辑
                    Debug.Log("异步计算Pass正常执行 - Bug已修复！");
                });
            }
            
            // 注意：没有任何后续Pass读取computeOutput
            // 修复前：这里会导致Pass被错误裁剪
            // 修复后：Pass会正常执行
        }

        /// <summary>
        /// 演示Load/Store Action自动推导功能
        /// 
        /// 新功能：通过EColorAccessFlag和EDepthAccessFlag自动推导最优的Load/Store Action
        /// 无需手动指定，系统会根据Pass依赖关系和访问模式自动优化
        /// </summary>
        public static void DemoAutoLoadStoreInference(RGBuilder builder, RGTextureRef colorTarget, RGTextureRef depthTarget)
        {
            // Pass 1: 第一个渲染Pass - 完全重写颜色缓冲区
            using (var pass1 = builder.AddRasterPass<EmptyPassData>(ProfilingSampler.Get("FirstRenderPass")))
            {
                // WriteAll表示完全重写，系统会自动推导为LoadAction.Clear或DontCare
                pass1.SetColorAttachment(colorTarget, 0, EColorAccessFlag.WriteAll);
                
                // ReadWrite表示读写深度，系统会根据是否有前序Pass自动推导LoadAction
                pass1.SetDepthStencilAttachment(depthTarget, EDepthAccessFlag.ReadWrite);
                
                pass1.SetExecuteFunc((in EmptyPassData data, in RGRasterEncoder cmd, RGObjectPool pool) =>
                {
                    Debug.Log("第一个Pass: 自动推导LoadAction为Clear/DontCare");
                });
            }

            // Pass 2: 混合渲染Pass - 在现有内容基础上渲染
            using (var pass2 = builder.AddRasterPass<EmptyPassData>(ProfilingSampler.Get("BlendRenderPass")))
            {
                // Write表示需要保留现有内容，系统会自动推导为LoadAction.Load
                pass2.SetColorAttachment(colorTarget, 0, EColorAccessFlag.Write);
                
                // ReadOnly表示只读深度，系统会自动推导为LoadAction.Load
                pass2.SetDepthStencilAttachment(depthTarget, EDepthAccessFlag.ReadOnly);
                
                pass2.SetExecuteFunc((in EmptyPassData data, in RGRasterEncoder cmd, RGObjectPool pool) =>
                {
                    Debug.Log("第二个Pass: 自动推导LoadAction为Load");
                });
            }

            // Pass 3: 临时效果Pass - 不关心精确像素
            using (var pass3 = builder.AddRasterPass<EmptyPassData>(ProfilingSampler.Get("EffectPass")))
            {
                // Discard表示不关心旧值，即使不是全屏写入也使用DontCare
                pass3.SetColorAttachment(colorTarget, 0, EColorAccessFlag.Discard);
                
                pass3.SetExecuteFunc((in EmptyPassData data, in RGRasterEncoder cmd, RGObjectPool pool) =>
                {
                    Debug.Log("第三个Pass: 使用Discard模式优化带宽");
                });
            }
            
            // 系统会自动推导StoreAction：
            // - 如果后续有Pass读取，使用Store
            // - 如果是最后使用或导入资源，使用Store
            // - 其他情况使用DontCare节省带宽
        }

        /// <summary>
        /// 演示Pass合并优化功能
        /// 
        /// 新功能：自动识别使用相同渲染目标的连续光栅Pass并合并到单个RenderPass中
        /// 减少RenderPass切换开销，优化GPU性能
        /// </summary>
        public static void DemoPassMerging(RGBuilder builder, RGTextureRef gBufferA, RGTextureRef gBufferB, RGTextureRef depthBuffer)
        {
            // Pass 1: GBuffer基础几何体渲染
            using (var geometryPass = builder.AddRasterPass<EmptyPassData>(ProfilingSampler.Get("GeometryPass")))
            {
                geometryPass.SetColorAttachment(gBufferA, 0, EColorAccessFlag.WriteAll);
                geometryPass.SetColorAttachment(gBufferB, 1, EColorAccessFlag.WriteAll);
                geometryPass.SetDepthStencilAttachment(depthBuffer, EDepthAccessFlag.ReadWrite);
                
                // 允许Pass合并（默认开启）
                geometryPass.EnablePassMerge(true);
                
                geometryPass.SetExecuteFunc((in EmptyPassData data, in RGRasterEncoder cmd, RGObjectPool pool) =>
                {
                    Debug.Log("几何体Pass: 渲染基础几何");
                });
            }

            // Pass 2: 贴花渲染 - 使用相同渲染目标
            using (var decalPass = builder.AddRasterPass<EmptyPassData>(ProfilingSampler.Get("DecalPass")))
            {
                // 相同的渲染目标配置
                decalPass.SetColorAttachment(gBufferA, 0, EColorAccessFlag.Write);
                decalPass.SetColorAttachment(gBufferB, 1, EColorAccessFlag.Write);
                decalPass.SetDepthStencilAttachment(depthBuffer, EDepthAccessFlag.ReadOnly);
                
                // 允许与前面的Pass合并
                decalPass.EnablePassMerge(true);
                
                decalPass.SetExecuteFunc((in EmptyPassData data, in RGRasterEncoder cmd, RGObjectPool pool) =>
                {
                    Debug.Log("贴花Pass: 在同一RenderPass内执行");
                });
            }

            // Pass 3: 植被渲染 - 使用相同渲染目标
            using (var vegetationPass = builder.AddRasterPass<EmptyPassData>(ProfilingSampler.Get("VegetationPass")))
            {
                vegetationPass.SetColorAttachment(gBufferA, 0, EColorAccessFlag.Write);
                vegetationPass.SetColorAttachment(gBufferB, 1, EColorAccessFlag.Write);
                vegetationPass.SetDepthStencilAttachment(depthBuffer, EDepthAccessFlag.ReadWrite);
                
                vegetationPass.EnablePassMerge(true);
                
                vegetationPass.SetExecuteFunc((in EmptyPassData data, in RGRasterEncoder cmd, RGObjectPool pool) =>
                {
                    Debug.Log("植被Pass: 继续在同一RenderPass内执行");
                });
            }

            // Pass 4: 特效Pass - 禁用合并
            using (var effectPass = builder.AddRasterPass<EmptyPassData>(ProfilingSampler.Get("EffectPass")))
            {
                effectPass.SetColorAttachment(gBufferA, 0, EColorAccessFlag.Write);
                effectPass.SetColorAttachment(gBufferB, 1, EColorAccessFlag.Write);
                effectPass.SetDepthStencilAttachment(depthBuffer, EDepthAccessFlag.ReadOnly);
                
                // 禁用合并 - 这将结束前面的合并组
                effectPass.EnablePassMerge(false);
                
                effectPass.SetExecuteFunc((in EmptyPassData data, in RGRasterEncoder cmd, RGObjectPool pool) =>
                {
                    Debug.Log("特效Pass: 在新的RenderPass中执行");
                });
            }
            
            // 结果：前三个Pass会被合并到一个RenderPass中
            // 第四个Pass会在单独的RenderPass中执行
            // 这样减少了RenderPass切换开销，提升了性能
        }

        /// <summary>
        /// 空的Pass数据结构，用于演示
        /// </summary>
        struct EmptyPassData
        {
            // 演示用的空结构体
        }

        /// <summary>
        /// 综合演示所有新功能的组合使用
        /// </summary>
        public static void DemoCompleteWorkflow(RGBuilder builder)
        {
            Debug.Log("=== RenderGraph重构功能综合演示 ===");
            
            // 创建测试纹理
            var colorTarget = builder.CreateTexture(new TextureDescriptor(1920, 1080)
            {
                name = "MainColorTarget",
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm
            });
            
            var depthTarget = builder.CreateTexture(new TextureDescriptor(1920, 1080)
            {
                name = "MainDepthTarget",
                depthBufferBits = EDepthBits.Depth32
            });
            
            var gBufferA = builder.CreateTexture(new TextureDescriptor(1920, 1080)
            {
                name = "GBufferA",
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm
            });
            
            var gBufferB = builder.CreateTexture(new TextureDescriptor(1920, 1080)
            {
                name = "GBufferB", 
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm
            });

            Debug.Log("1. 演示异步计算Pass裁剪修复");
            DemoAsyncComputeCullingFix(builder);
            
            Debug.Log("2. 演示Load/Store Action自动推导");
            DemoAutoLoadStoreInference(builder, colorTarget, depthTarget);
            
            Debug.Log("3. 演示Pass合并优化");
            DemoPassMerging(builder, gBufferA, gBufferB, depthTarget);
            
            Debug.Log("=== 演示完成 ===");
        }
    }
}