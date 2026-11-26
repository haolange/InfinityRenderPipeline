using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.AnalyticShadow;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    internal struct AnalyticShadowInputs
    {
        public bool active;
        public RGBufferHandle recordsHandle;
        public RGBufferHandle indirectHeaderHandle;
        public RGBufferHandle indirectIndexHandle;
        public RGBufferHandle directHeaderHandle;
        public RGBufferHandle directIndexHandle;

        public static AnalyticShadowInputs Query(RGBuilder graph, in ViewContext context)
        {
            bool active = context.analyticShadows != null && context.analyticShadows.OccluderCount > 0;
            if (!active)
            {
                return default;
            }
            return new AnalyticShadowInputs
            {
                active = true,
                recordsHandle = graph.scoper.QueryBuffer(AnalyticShadowShaderIDs.AnalyticOccluderRG),
                indirectHeaderHandle = graph.scoper.QueryBuffer(AnalyticShadowShaderIDs.AnalyticIndirectHeaderRG),
                indirectIndexHandle = graph.scoper.QueryBuffer(AnalyticShadowShaderIDs.AnalyticIndirectIndexRG),
                directHeaderHandle = graph.scoper.QueryBuffer(AnalyticShadowShaderIDs.AnalyticDirectHeaderRG),
                directIndexHandle = graph.scoper.QueryBuffer(AnalyticShadowShaderIDs.AnalyticDirectIndexRG)
            };
        }

        public void Read<THandle>(THandle passHandle) where THandle : struct, IPassHandle
        {
            if (!active)
            {
                return;
            }
            passHandle.ReadBuffer(recordsHandle);
            passHandle.ReadBuffer(indirectHeaderHandle);
            passHandle.ReadBuffer(indirectIndexHandle);
            passHandle.ReadBuffer(directHeaderHandle);
            passHandle.ReadBuffer(directIndexHandle);
        }

        public void Bind<TCommands>(in TCommands commands, ComputeShader shader, int kernel) where TCommands : struct, IComputeCommands
        {
            if (!active)
            {
                return;
            }
            commands.SetComputeBufferParam(shader, kernel, AnalyticShadowShaderIDs.AnalyticOccluderBuffer, recordsHandle);
            commands.SetComputeBufferParam(shader, kernel, AnalyticShadowShaderIDs.AnalyticIndirectClusterHeader, indirectHeaderHandle);
            commands.SetComputeBufferParam(shader, kernel, AnalyticShadowShaderIDs.AnalyticIndirectClusterIndices, indirectIndexHandle);
            commands.SetComputeBufferParam(shader, kernel, AnalyticShadowShaderIDs.AnalyticDirectClusterHeader, directHeaderHandle);
            commands.SetComputeBufferParam(shader, kernel, AnalyticShadowShaderIDs.AnalyticDirectClusterIndices, directIndexHandle);
        }

        public void BindGlobal<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
        {
            if (!active)
            {
                return;
            }
            commands.SetGlobalBuffer(AnalyticShadowShaderIDs.AnalyticOccluderBuffer, recordsHandle);
            commands.SetGlobalBuffer(AnalyticShadowShaderIDs.AnalyticIndirectClusterHeader, indirectHeaderHandle);
            commands.SetGlobalBuffer(AnalyticShadowShaderIDs.AnalyticIndirectClusterIndices, indirectIndexHandle);
            commands.SetGlobalBuffer(AnalyticShadowShaderIDs.AnalyticDirectClusterHeader, directHeaderHandle);
            commands.SetGlobalBuffer(AnalyticShadowShaderIDs.AnalyticDirectClusterIndices, directIndexHandle);
        }
    }

    internal static class AnalyticShadowPassUtilityData
    {
        internal static readonly GlobalKeyword ForwardAnalyticShadowKeyword = GlobalKeyword.Create("INFINITY_FORWARD_ANALYTIC_SHADOW");
        internal static readonly ProfilingSampler UploadSampler = new ProfilingSampler("UploadAnalyticShadowData");
    }

    internal sealed class AnalyticShadowPass
    {
        readonly RGBuilder m_RGBuilder;

        internal AnalyticShadowPass(RGBuilder graph)
        {
            m_RGBuilder = graph;
        }

        struct UploadAnalyticShadowDataPassData
        {
            public AnalyticShadowContext context;
            public RGBufferHandle recordBufferHandle;
            public RGBufferHandle indirectHeaderHandle;
            public RGBufferHandle indirectIndexHandle;
            public RGBufferHandle directHeaderHandle;
            public RGBufferHandle directIndexHandle;
        }

        internal void UploadAnalyticShadowData(AnalyticShadowContext shadowContext)
        {
            if (shadowContext == null || shadowContext.OccluderCount == 0)
            {
                return;
            }
            RGTransferPassHandle<UploadAnalyticShadowDataPassData> passHandle =
                m_RGBuilder.AddTransferPass<UploadAnalyticShadowDataPassData>(AnalyticShadowPassUtilityData.UploadSampler);
            passHandle.EnablePassCulling(false);
            ref UploadAnalyticShadowDataPassData passData = ref passHandle.GetPassData();
            passData.context = shadowContext;
            passData.recordBufferHandle = passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(shadowContext.RecordBuffer, "AnalyticOccluderBuffer"));
            passData.indirectHeaderHandle = passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(shadowContext.IndirectHeaderBuffer, "AnalyticIndirectClusterHeader"));
            passData.indirectIndexHandle = passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(shadowContext.IndirectIndexBuffer, "AnalyticIndirectClusterIndices"));
            passData.directHeaderHandle = passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(shadowContext.DirectHeaderBuffer, "AnalyticDirectClusterHeader"));
            passData.directIndexHandle = passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(shadowContext.DirectIndexBuffer, "AnalyticDirectClusterIndices"));

            m_RGBuilder.scoper.RegisterBuffer(AnalyticShadowShaderIDs.AnalyticOccluderRG, passData.recordBufferHandle);
            m_RGBuilder.scoper.RegisterBuffer(AnalyticShadowShaderIDs.AnalyticIndirectHeaderRG, passData.indirectHeaderHandle);
            m_RGBuilder.scoper.RegisterBuffer(AnalyticShadowShaderIDs.AnalyticIndirectIndexRG, passData.indirectIndexHandle);
            m_RGBuilder.scoper.RegisterBuffer(AnalyticShadowShaderIDs.AnalyticDirectHeaderRG, passData.directHeaderHandle);
            m_RGBuilder.scoper.RegisterBuffer(AnalyticShadowShaderIDs.AnalyticDirectIndexRG, passData.directIndexHandle);

            passHandle.SetExecuteFunc((in UploadAnalyticShadowDataPassData passData, in RGTransferEncoder commands) =>
            {
                passData.context.Upload(commands);
            });
        }
    }
}
