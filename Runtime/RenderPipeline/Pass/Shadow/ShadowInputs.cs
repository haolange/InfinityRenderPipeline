using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ShadowShaderIDs
    {
        internal static readonly int SRV_CascadeShadowMap = Shader.PropertyToID("SRV_CascadeShadowMap");
        internal static readonly int SRV_LocalShadowMap = Shader.PropertyToID("SRV_LocalShadowMap");
        internal static readonly int SRV_CascadeShadowMoments = Shader.PropertyToID("SRV_CascadeShadowMoments");
        internal static readonly int SRV_LocalShadowMoments = Shader.PropertyToID("SRV_LocalShadowMoments");
        internal static readonly int LocalShadowMapSize = Shader.PropertyToID("_LocalShadowMapSize");
        internal static readonly int ShadowMomentParams = Shader.PropertyToID("_ShadowMomentParams");
        internal static readonly int ShadowmaskMode = Shader.PropertyToID("_InfinityShadowmaskMode");
        internal static readonly int ShadowDistance = Shader.PropertyToID("_InfinityShadowDistance");
    }

    // Every shadow resource and constant Shaders/ShaderLibrary/ShadowSampling.hlsl reads. Query once at record time
    // (QueryShadowInputs), declare the reads on the consuming pass (Read), then bind in the execute function (Bind for
    // one compute kernel, BindGlobal for raster passes). Moment atlases fall back to a black texture on frames where
    // no visible light uses VSM / EVSM; no shader samples them then.
    internal struct FShadowInputs
    {
        public RGTextureHandle cascadeMap;
        public RGTextureHandle localMap;
        public RGTextureHandle cascadeMoments;
        public RGTextureHandle localMoments;
        public bool hasCascadeMoments;
        public bool hasLocalMoments;
        public RGBufferHandle localMatrices;
        public RGBufferHandle localSlices;
        public int cascadeCount;
        public int shadowmaskMode;
        public float shadowDistance;
        public Matrix4x4[] cascadeMatrices;
        public Vector4[] cascadeSpheres;
        public Vector4[] cascadeParams;
        public Vector4 cascadeSplitDistances;
        public Vector4 cascadeMapSize;
        public Vector4 localMapSize;
        public Vector4 filterParams;
        public Vector4 momentParams;

        public void Read(RGComputePassHandle passRef)
        {
            passRef.ReadTexture(cascadeMap);
            passRef.ReadTexture(localMap);
            if (hasCascadeMoments) passRef.ReadTexture(cascadeMoments);
            if (hasLocalMoments) passRef.ReadTexture(localMoments);
            passRef.ReadBuffer(localMatrices);
            passRef.ReadBuffer(localSlices);
        }

        public void Read(RGRasterPassHandle passRef)
        {
            passRef.ReadTexture(cascadeMap);
            passRef.ReadTexture(localMap);
            if (hasCascadeMoments) passRef.ReadTexture(cascadeMoments);
            if (hasLocalMoments) passRef.ReadTexture(localMoments);
            passRef.ReadBuffer(localMatrices);
            passRef.ReadBuffer(localSlices);
        }

        public readonly void Bind<TCommands>(in TCommands commands, ComputeShader shader, int kernel) where TCommands : struct, IComputeCommands
        {
            commands.SetComputeTextureParam(shader, kernel, ShadowShaderIDs.SRV_CascadeShadowMap, cascadeMap);
            commands.SetComputeTextureParam(shader, kernel, ShadowShaderIDs.SRV_LocalShadowMap, localMap);
            commands.SetComputeTextureParam(shader, kernel, ShadowShaderIDs.SRV_CascadeShadowMoments, hasCascadeMoments ? (RenderTargetIdentifier)cascadeMoments : Texture2D.blackTexture);
            commands.SetComputeTextureParam(shader, kernel, ShadowShaderIDs.SRV_LocalShadowMoments, hasLocalMoments ? (RenderTargetIdentifier)localMoments : Texture2D.blackTexture);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.LocalShadowMatrixBuffer, localMatrices);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.LocalShadowRectBuffer, localSlices);
            commands.SetComputeIntParam(shader, CascadeShadowPassUtilityData.CascadeCountID, cascadeCount);
            commands.SetComputeMatrixArrayParam(shader, CascadeShadowPassUtilityData.CascadeMatricesID, cascadeMatrices);
            commands.SetComputeVectorArrayParam(shader, CascadeShadowPassUtilityData.CascadeSpheresID, cascadeSpheres);
            commands.SetComputeVectorArrayParam(shader, CascadeShadowPassUtilityData.CascadeShadowParamsID, cascadeParams);
            commands.SetComputeVectorParam(shader, CascadeShadowPassUtilityData.CascadeSplitDistancesID, cascadeSplitDistances);
            commands.SetComputeVectorParam(shader, CascadeShadowPassUtilityData.CascadeShadowMapSizeID, cascadeMapSize);
            commands.SetComputeVectorParam(shader, ShadowShaderIDs.LocalShadowMapSize, localMapSize);
            commands.SetComputeVectorParam(shader, CascadeShadowPassUtilityData.ShadowFilterParamsID, filterParams);
            commands.SetComputeVectorParam(shader, ShadowShaderIDs.ShadowMomentParams, momentParams);
            commands.SetComputeIntParam(shader, ShadowShaderIDs.ShadowmaskMode, shadowmaskMode);
            commands.SetComputeFloatParam(shader, ShadowShaderIDs.ShadowDistance, shadowDistance);
        }

        public readonly void BindGlobal<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
        {
            commands.SetGlobalTexture(ShadowShaderIDs.SRV_CascadeShadowMap, cascadeMap);
            commands.SetGlobalTexture(ShadowShaderIDs.SRV_LocalShadowMap, localMap);
            commands.SetGlobalTexture(ShadowShaderIDs.SRV_CascadeShadowMoments, hasCascadeMoments ? (RenderTargetIdentifier)cascadeMoments : Texture2D.blackTexture);
            commands.SetGlobalTexture(ShadowShaderIDs.SRV_LocalShadowMoments, hasLocalMoments ? (RenderTargetIdentifier)localMoments : Texture2D.blackTexture);
            commands.SetGlobalBuffer(LightShaderIDs.LocalShadowMatrixBuffer, localMatrices);
            commands.SetGlobalBuffer(LightShaderIDs.LocalShadowRectBuffer, localSlices);
            commands.SetGlobalInt(CascadeShadowPassUtilityData.CascadeCountID, cascadeCount);
            commands.SetGlobalMatrixArray(CascadeShadowPassUtilityData.CascadeMatricesID, cascadeMatrices);
            commands.SetGlobalVectorArray(CascadeShadowPassUtilityData.CascadeSpheresID, cascadeSpheres);
            commands.SetGlobalVectorArray(CascadeShadowPassUtilityData.CascadeShadowParamsID, cascadeParams);
            commands.SetGlobalVector(CascadeShadowPassUtilityData.CascadeSplitDistancesID, cascadeSplitDistances);
            commands.SetGlobalVector(CascadeShadowPassUtilityData.CascadeShadowMapSizeID, cascadeMapSize);
            commands.SetGlobalVector(ShadowShaderIDs.LocalShadowMapSize, localMapSize);
            commands.SetGlobalVector(CascadeShadowPassUtilityData.ShadowFilterParamsID, filterParams);
            commands.SetGlobalVector(ShadowShaderIDs.ShadowMomentParams, momentParams);
            commands.SetGlobalInt(ShadowShaderIDs.ShadowmaskMode, shadowmaskMode);
            commands.SetGlobalFloat(ShadowShaderIDs.ShadowDistance, shadowDistance);
        }
    }

    public partial class InfinityRenderPipeline
    {
        FShadowInputs QueryShadowInputs(RenderContext renderContext)
        {
            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            FShadowFilterSettings filter = pipelineAsset.ResolveShadowFilter();
            // The cascade atlas holds 2x2 tiles of cascadeShadowMapResolution.
            float cascadeSize = pipelineAsset.cascadeShadowMapResolution * 2;
            float localSize = pipelineAsset.localShadowMapResolution;
            var inputs = new FShadowInputs
            {
                cascadeMap = m_RGScoper.QueryTexture(InfinityShaderIDs.CascadeShadowMap),
                localMap = m_RGScoper.QueryTexture(InfinityShaderIDs.LocalShadowMap),
                localMatrices = m_RGScoper.QueryBuffer(LightShaderIDs.LocalShadowMatrixBuffer),
                localSlices = m_RGScoper.QueryBuffer(LightShaderIDs.LocalShadowRectBuffer),
                cascadeCount = allocator.CascadeAllocatedCount,
                cascadeMatrices = m_RGBuilder.SnapshotRecordingArray(allocator.CascadeMatrices),
                cascadeSpheres = m_RGBuilder.SnapshotRecordingArray(allocator.CascadeSpheres),
                cascadeParams = m_RGBuilder.SnapshotRecordingArray(allocator.CascadeParams),
                cascadeSplitDistances = allocator.CascadeSplitDistances,
                cascadeMapSize = new Vector4(cascadeSize, cascadeSize, 1 / cascadeSize, 1 / cascadeSize),
                localMapSize = new Vector4(localSize, localSize, 1 / localSize, 1 / localSize),
                filterParams = CascadeShadowPassUtilityData.FilterParams(filter),
                momentParams = ShadowMomentPassUtilityData.SamplingParams(filter),
                shadowmaskMode = QualitySettings.shadowmaskMode == ShadowmaskMode.DistanceShadowmask ? 1 : 0,
                shadowDistance = pipelineAsset.shadowDistance,
            };
            inputs.hasCascadeMoments = m_RGScoper.TryQueryTexture(InfinityShaderIDs.CascadeShadowMoments, out inputs.cascadeMoments);
            inputs.hasLocalMoments = m_RGScoper.TryQueryTexture(InfinityShaderIDs.LocalShadowMoments, out inputs.localMoments);
            return inputs;
        }
    }
}
