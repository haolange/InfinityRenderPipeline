using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        static readonly ProfilingSampler s_LightUpload = new ProfilingSampler("UploadLightData");
        struct LightUploadPassData { public LightContext context; }

        void UploadLightData(LightContext context)
        {
            using (RGTransferPassRef pass = m_RGBuilder.AddTransferPass<LightUploadPassData>(s_LightUpload))
            {
                pass.GetPassData<LightUploadPassData>().context = context;
                m_RGScoper.RegisterBuffer(LightShaderIDs.LightRecordBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.LightRecordBuffer, "LightRecordBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.LightBoundsBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.LightBoundsBuffer, "LightBoundsBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.LocalShadowMatrixBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.LocalShadowMatrixBuffer, "LocalShadowMatrixBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.LocalShadowRectBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.LocalShadowRectBuffer, "LocalShadowRectBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.EmptyTileRangeBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.EmptyTileRangeBuffer, "EmptyTileRangeBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.EmptyTileListBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.EmptyTileListBuffer, "EmptyTileListBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.EmptyZBinRangeBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.EmptyZBinRangeBuffer, "EmptyZBinRangeBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.EmptyZBinListBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.EmptyZBinListBuffer, "EmptyZBinListBuffer")));
                pass.SetExecuteFunc((in LightUploadPassData data, in RGTransferEncoder commands, RGObjectPool pool) => data.context.Upload(commands));
            }
        }
    }
}
