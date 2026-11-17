using System.Runtime.InteropServices;
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
        internal static readonly GlobalKeyword CascadeMoments = GlobalKeyword.Create("INFINITY_CASCADE_SHADOW_MOMENTS");
        internal static readonly GlobalKeyword LocalMoments = GlobalKeyword.Create("INFINITY_LOCAL_SHADOW_MOMENTS");
    }

    // The camera's shadow constants as Shaders/Library/ShadowSampling.hlsl reads them, uploaded once per camera before
    // its graph (ShadowInputs.ResolveUniform) and bound globally for raster and compute passes.
    // GPU-RECORD: ShadowUniform Shaders/Library/ShadowSampling.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShadowUniform
    {
        public Matrix4x4 cascadeMatrix0, cascadeMatrix1, cascadeMatrix2, cascadeMatrix3;
        public Vector4 cascadeSphere0, cascadeSphere1, cascadeSphere2, cascadeSphere3;
        public Vector4 cascadeParameters0, cascadeParameters1, cascadeParameters2, cascadeParameters3;
        public Vector4 cascadeSplitDistances;
        public Vector4 cascadeMapSize;
        public Vector4 localMapSize;
        public Vector4 filterParameters;
        public Vector4 momentParameters;
        public int cascadeCount;
        public int shadowmaskMode;
        public float shadowDistance;
        public int padding;
    }

    // Every shadow resource Shaders/Library/ShadowSampling.hlsl reads. Query once at record time (Query),
    // declare the reads on the consuming pass (Read), then bind in the execute function (Bind for one compute kernel,
    // BindGlobal for raster passes). The constants are the camera's ShadowUniform. Moment atlases fall back to a black texture on frames where
    // no visible light uses VSM / EVSM; no shader samples them then.
    internal struct ShadowInputs
    {
        public RGTextureHandle cascadeMapHandle;
        public RGTextureHandle localMapHandle;
        public RGTextureHandle cascadeMomentsHandle;
        public RGTextureHandle localMomentsHandle;
        public bool hasCascadeMoments;
        public bool hasLocalMoments;
        public RGBufferHandle localMatricesHandle;
        public RGBufferHandle localSlicesHandle;

        public void Read<THandle>(THandle passHandle) where THandle : struct, IPassHandle
        {
            passHandle.ReadTexture(cascadeMapHandle);
            passHandle.ReadTexture(localMapHandle);
            if (hasCascadeMoments)
            {
                passHandle.ReadTexture(cascadeMomentsHandle);
            }
            if (hasLocalMoments)
            {
                passHandle.ReadTexture(localMomentsHandle);
            }
            passHandle.ReadBuffer(localMatricesHandle);
            passHandle.ReadBuffer(localSlicesHandle);
        }

        public readonly void Bind<TCommands>(in TCommands commands, ComputeShader shader, int kernel) where TCommands : struct, IComputeCommands
        {
            commands.SetKeyword(ShadowShaderIDs.CascadeMoments, hasCascadeMoments);
            commands.SetKeyword(ShadowShaderIDs.LocalMoments, hasLocalMoments);
            commands.SetComputeTextureParam(shader, kernel, ShadowShaderIDs.SRV_CascadeShadowMap, cascadeMapHandle);
            commands.SetComputeTextureParam(shader, kernel, ShadowShaderIDs.SRV_LocalShadowMap, localMapHandle);
            if (hasCascadeMoments)
                commands.SetComputeTextureParam(shader, kernel, ShadowShaderIDs.SRV_CascadeShadowMoments, cascadeMomentsHandle);
            if (hasLocalMoments)
                commands.SetComputeTextureParam(shader, kernel, ShadowShaderIDs.SRV_LocalShadowMoments, localMomentsHandle);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.LocalShadowMatrixBuffer, localMatricesHandle);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.LocalShadowRectBuffer, localSlicesHandle);
        }

        public readonly void BindGlobal<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
        {
            commands.SetKeyword(ShadowShaderIDs.CascadeMoments, hasCascadeMoments);
            commands.SetKeyword(ShadowShaderIDs.LocalMoments, hasLocalMoments);
            commands.SetGlobalTexture(ShadowShaderIDs.SRV_CascadeShadowMap, cascadeMapHandle);
            commands.SetGlobalTexture(ShadowShaderIDs.SRV_LocalShadowMap, localMapHandle);
            if (hasCascadeMoments)
                commands.SetGlobalTexture(ShadowShaderIDs.SRV_CascadeShadowMoments, cascadeMomentsHandle);
            if (hasLocalMoments)
                commands.SetGlobalTexture(ShadowShaderIDs.SRV_LocalShadowMoments, localMomentsHandle);
            commands.SetGlobalBuffer(LightShaderIDs.LocalShadowMatrixBuffer, localMatricesHandle);
            commands.SetGlobalBuffer(LightShaderIDs.LocalShadowRectBuffer, localSlicesHandle);
        }

        // The camera's shadow constants, from the allocator the light context built after culling.
        internal static ShadowUniform ResolveUniform(in ViewContext context, in InfinityRenderPipelineAsset.Settings settings)
        {
            ShadowAllocator allocator = context.lights.ShadowAllocator;
            ShadowFilterSettings filter = settings.shadowFilter;
            // The cascade atlas holds 2x2 tiles of cascadeShadowMapResolution.
            float cascadeSize = settings.cascadeShadowMapResolution * 2;
            float localSize = settings.localShadowMapResolution;
            Matrix4x4[] matrices = allocator.CascadeMatrices;
            Vector4[] spheres = allocator.CascadeSpheres;
            Vector4[] cascades = allocator.CascadeParams;
            return new ShadowUniform
            {
                cascadeMatrix0 = matrices[0], cascadeMatrix1 = matrices[1], cascadeMatrix2 = matrices[2], cascadeMatrix3 = matrices[3],
                cascadeSphere0 = spheres[0], cascadeSphere1 = spheres[1], cascadeSphere2 = spheres[2], cascadeSphere3 = spheres[3],
                cascadeParameters0 = cascades[0], cascadeParameters1 = cascades[1], cascadeParameters2 = cascades[2], cascadeParameters3 = cascades[3],
                cascadeSplitDistances = allocator.CascadeSplitDistances,
                cascadeMapSize = new Vector4(cascadeSize, cascadeSize, 1 / cascadeSize, 1 / cascadeSize),
                localMapSize = new Vector4(localSize, localSize, 1 / localSize, 1 / localSize),
                filterParameters = CascadeShadowPassUtilityData.FilterParams(filter),
                momentParameters = ShadowMomentPassUtilityData.SamplingParams(filter),
                cascadeCount = allocator.CascadeAllocatedCount,
                shadowmaskMode = context.shadowmaskMode == ShadowmaskMode.DistanceShadowmask ? 1 : 0,
                shadowDistance = settings.shadowDistance
            };
        }

        internal static ShadowInputs Query(RGBuilder graph, in ViewContext context)
        {
            ShadowAllocator allocator = context.lights.ShadowAllocator;
            var inputs = new ShadowInputs
            {
                cascadeMapHandle = graph.scoper.QueryTexture(InfinityShaderIDs.CascadeShadowMap),
                localMapHandle = graph.scoper.QueryTexture(InfinityShaderIDs.LocalShadowMap),
                localMatricesHandle = graph.scoper.QueryBuffer(LightShaderIDs.LocalShadowMatrixBuffer),
                localSlicesHandle = graph.scoper.QueryBuffer(LightShaderIDs.LocalShadowRectBuffer),
            };
            // ComputeShadowMoments builds each atlas exactly when the feature records and the allocator uses moments there.
            bool moments = context.features.ShouldRecord(EFrameFeature.ShadowMoments);
            inputs.hasCascadeMoments = moments && allocator.CascadeUsesMoments;
            inputs.hasLocalMoments = moments && allocator.LocalMomentSliceCount > 0;
            inputs.cascadeMomentsHandle = inputs.hasCascadeMoments ? graph.scoper.QueryTexture(InfinityShaderIDs.CascadeShadowMoments) : default;
            inputs.localMomentsHandle = inputs.hasLocalMoments ? graph.scoper.QueryTexture(InfinityShaderIDs.LocalShadowMoments) : default;
            return inputs;
        }
    }
}
