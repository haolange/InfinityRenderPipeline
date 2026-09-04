using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ZBinningPassUtilityData
    {
        internal static string ZBinRangeName = "ZBinRangeBuffer";
        internal static string ZBinListName = "ZBinLightListBuffer";
        internal static string ZBinCountName = "ZBinCountBuffer";
        internal static string TileRangeName = "TileLightRangeBuffer";
        internal static string TileListName = "TileLightListBuffer";
        internal static string TileCountName = "TileCountBuffer";
        internal static int ZBin_ScreenSizeID = Shader.PropertyToID("ZBin_ScreenSize");
        internal static int ZBin_TileSizeID = Shader.PropertyToID("ZBin_TileSize");
        internal static int ZBin_NumTilesID = Shader.PropertyToID("ZBin_NumTiles");
        internal static int ZBin_NearFarID = Shader.PropertyToID("ZBin_NearFar");
        internal static int ZBin_NumBinsID = Shader.PropertyToID("ZBin_NumBins");
        internal static int ZBin_LightCountID = Shader.PropertyToID("ZBin_LightCount");
        internal static int ZBin_DirectionalCountID = Shader.PropertyToID("ZBin_DirectionalCount");
        internal static int ZBin_PhaseID = Shader.PropertyToID("ZBin_Phase");
        internal static int ZBin_MaxTileListID = Shader.PropertyToID("ZBin_MaxTileList");
        internal static int ZBin_MaxZBinListID = Shader.PropertyToID("ZBin_MaxZBinList");
        internal static int Matrix_ViewID = Shader.PropertyToID("Matrix_View");
        internal static int Matrix_ProjID = Shader.PropertyToID("Matrix_Proj");
        internal static int SRV_LightBoundsBufferID = Shader.PropertyToID("SRV_LightBoundsBuffer");
        internal static int UAV_TileCountID = Shader.PropertyToID("UAV_TileCount");
        internal static int UAV_TileRangeID = Shader.PropertyToID("UAV_TileRange");
        internal static int UAV_TileLightListID = Shader.PropertyToID("UAV_TileLightList");
        internal static int UAV_ZBinCountID = Shader.PropertyToID("UAV_ZBinCount");
        internal static int UAV_ZBinRangeID = Shader.PropertyToID("UAV_ZBinRange");
        internal static int UAV_ZBinLightListID = Shader.PropertyToID("UAV_ZBinLightList");
        internal static int UAV_OverflowCounterID = Shader.PropertyToID("UAV_OverflowCounter");
        internal static int SRV_TileLightRangeID = Shader.PropertyToID("SRV_TileLightRange");
        internal static int SRV_TileLightListID = Shader.PropertyToID("SRV_TileLightList");
        internal static int SRV_ZBinRangeID = Shader.PropertyToID("SRV_ZBinRange");
        internal static int SRV_ZBinLightListID = Shader.PropertyToID("SRV_ZBinLightList");
        internal static int KernelLightCount = 0;
        internal static int KernelPrefixSum = 1;
        internal static int KernelFill = 2;
        internal const int NumBins = 32;
        internal const int MaxLightsPerTile = 64;
    }

    public partial class InfinityRenderPipeline
    {
        struct ZBinningPassData
        {
            public int tileSize;
            public int2 screenSize;
            public int2 numTiles;
            public int numBins;
            public int lightCount;
            public int directionalCount;
            public int maxTileList;
            public int maxZBinList;
            public float nearPlane;
            public float farPlane;
            public Matrix4x4 matrixView;
            public Matrix4x4 matrixProj;
            public ComputeShader zBinningShader;
            public GraphicsBuffer lightBoundsBuffer;
            public GraphicsBuffer overflowBuffer;
            public RGBufferRef tileCount;
            public RGBufferRef tileRange;
            public RGBufferRef tileLightList;
            public RGBufferRef zBinCount;
            public RGBufferRef zBinRange;
            public RGBufferRef zBinLightList;
        }

        static bool HasZBinningLightList(RenderContext renderContext)
        {
            return renderContext != null && renderContext.lightContext != null && renderContext.lightContext.HasZBinningLightList();
        }

        void ComputeZBinningLightList(RenderContext renderContext, Camera camera)
        {
            if (!HasZBinningLightList(renderContext))
            {
                return;
            }

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.zBinningShader, "LightCount", "PrefixSum", "Fill"))
            {
                return;
            }

            int tileSize = 16;
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            int numTilesX = Mathf.CeilToInt((float)width / tileSize);
            int numTilesY = Mathf.CeilToInt((float)height / tileSize);
            int numTiles = numTilesX * numTilesY;
            int numBins = ZBinningPassUtilityData.NumBins;
            int lightCount = renderContext.lightContext.LocalLightCount;
            int maxTileList = math.max(numTiles * ZBinningPassUtilityData.MaxLightsPerTile, 1);
            int maxZBinList = math.max(numBins * math.max(lightCount, 1), 1);

            RGBufferRef tileCount = m_RGScoper.CreateBuffer(InfinityShaderIDs.TileLightCountBuffer, new BufferDescriptor
            {
                name = ZBinningPassUtilityData.TileCountName,
                count = math.max(numTiles, 1),
                stride = sizeof(uint),
                type = ComputeBufferType.Structured
            });
            RGBufferRef tileRange = m_RGScoper.CreateBuffer(InfinityShaderIDs.TileLightRangeBuffer, new BufferDescriptor
            {
                name = ZBinningPassUtilityData.TileRangeName,
                count = math.max(numTiles, 1),
                stride = sizeof(uint) * 2,
                type = ComputeBufferType.Structured
            });
            RGBufferRef tileLightList = m_RGScoper.CreateBuffer(InfinityShaderIDs.TileLightListBuffer, new BufferDescriptor
            {
                name = ZBinningPassUtilityData.TileListName,
                count = maxTileList,
                stride = sizeof(uint),
                type = ComputeBufferType.Structured
            });
            RGBufferRef zBinCount = m_RGScoper.CreateBuffer(InfinityShaderIDs.ZBinCountBuffer, new BufferDescriptor
            {
                name = ZBinningPassUtilityData.ZBinCountName,
                count = numBins,
                stride = sizeof(uint),
                type = ComputeBufferType.Structured
            });
            RGBufferRef zBinRange = m_RGScoper.CreateBuffer(InfinityShaderIDs.ZBinRangeBuffer, new BufferDescriptor
            {
                name = ZBinningPassUtilityData.ZBinRangeName,
                count = numBins,
                stride = sizeof(uint) * 2,
                type = ComputeBufferType.Structured
            });
            RGBufferRef zBinLightList = m_RGScoper.CreateBuffer(InfinityShaderIDs.ZBinLightListBuffer, new BufferDescriptor
            {
                name = ZBinningPassUtilityData.ZBinListName,
                count = maxZBinList,
                stride = sizeof(uint),
                type = ComputeBufferType.Structured
            });

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<ZBinningPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeZBinningLightList)))
            {
                ref ZBinningPassData passData = ref passRef.GetPassData<ZBinningPassData>();
                passData.tileSize = tileSize;
                passData.screenSize = new int2(width, height);
                passData.numTiles = new int2(numTilesX, numTilesY);
                passData.numBins = numBins;
                passData.lightCount = lightCount;
                passData.directionalCount = renderContext.lightContext.DirectionalLightCount;
                passData.maxTileList = maxTileList;
                passData.maxZBinList = maxZBinList;
                passData.nearPlane = camera.nearClipPlane;
                passData.farPlane = camera.farClipPlane;
                passData.matrixView = camera.worldToCameraMatrix;
                passData.matrixProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                passData.zBinningShader = pipelineAsset.zBinningShader;
                passData.lightBoundsBuffer = renderContext.lightContext.LightBoundsBuffer;
                passData.overflowBuffer = renderContext.lightContext.ZBinOverflowBuffer;
                passData.tileCount = passRef.WriteBuffer(tileCount);
                passData.tileRange = passRef.WriteBuffer(tileRange);
                passData.tileLightList = passRef.WriteBuffer(tileLightList);
                passData.zBinCount = passRef.WriteBuffer(zBinCount);
                passData.zBinRange = passRef.WriteBuffer(zBinRange);
                passData.zBinLightList = passRef.WriteBuffer(zBinLightList);

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in ZBinningPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ZBinningPassData data = passData;
                    ComputeShader shader = data.zBinningShader;
                    RGComputeEncoder encoder = cmdEncoder;
                    int clearGroups = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(data.numTiles.x * data.numTiles.y, data.numBins) / 64.0f));
                    int lightGroups = Mathf.Max(1, Mathf.CeilToInt(data.lightCount / 64.0f));

                    void BindCommon(int kernel)
                    {
                        encoder.SetComputeVectorParam(shader, ZBinningPassUtilityData.ZBin_ScreenSizeID, new Vector4(data.screenSize.x, data.screenSize.y, 1.0f / data.screenSize.x, 1.0f / data.screenSize.y));
                        encoder.SetComputeIntParam(shader, ZBinningPassUtilityData.ZBin_TileSizeID, data.tileSize);
                        encoder.SetComputeVectorParam(shader, ZBinningPassUtilityData.ZBin_NumTilesID, new Vector4(data.numTiles.x, data.numTiles.y, 0, 0));
                        encoder.SetComputeVectorParam(shader, ZBinningPassUtilityData.ZBin_NearFarID, new Vector4(data.nearPlane, data.farPlane, 0, 0));
                        encoder.SetComputeIntParam(shader, ZBinningPassUtilityData.ZBin_NumBinsID, data.numBins);
                        encoder.SetComputeIntParam(shader, ZBinningPassUtilityData.ZBin_LightCountID, data.lightCount);
                        encoder.SetComputeIntParam(shader, ZBinningPassUtilityData.ZBin_DirectionalCountID, data.directionalCount);
                        encoder.SetComputeIntParam(shader, ZBinningPassUtilityData.ZBin_MaxTileListID, data.maxTileList);
                        encoder.SetComputeIntParam(shader, ZBinningPassUtilityData.ZBin_MaxZBinListID, data.maxZBinList);
                        encoder.SetComputeMatrixParam(shader, ZBinningPassUtilityData.Matrix_ViewID, data.matrixView);
                        encoder.SetComputeMatrixParam(shader, ZBinningPassUtilityData.Matrix_ProjID, data.matrixProj);
                        encoder.SetComputeBufferParam(shader, kernel, ZBinningPassUtilityData.SRV_LightBoundsBufferID, data.lightBoundsBuffer);
                        encoder.SetComputeBufferParam(shader, kernel, ZBinningPassUtilityData.UAV_TileCountID, data.tileCount);
                        encoder.SetComputeBufferParam(shader, kernel, ZBinningPassUtilityData.UAV_TileRangeID, data.tileRange);
                        encoder.SetComputeBufferParam(shader, kernel, ZBinningPassUtilityData.UAV_TileLightListID, data.tileLightList);
                        encoder.SetComputeBufferParam(shader, kernel, ZBinningPassUtilityData.UAV_ZBinCountID, data.zBinCount);
                        encoder.SetComputeBufferParam(shader, kernel, ZBinningPassUtilityData.UAV_ZBinRangeID, data.zBinRange);
                        encoder.SetComputeBufferParam(shader, kernel, ZBinningPassUtilityData.UAV_ZBinLightListID, data.zBinLightList);
                        encoder.SetComputeBufferParam(shader, kernel, ZBinningPassUtilityData.UAV_OverflowCounterID, data.overflowBuffer);
                    }

                    BindCommon(ZBinningPassUtilityData.KernelLightCount);
                    cmdEncoder.SetComputeIntParam(shader, ZBinningPassUtilityData.ZBin_PhaseID, 0);
                    cmdEncoder.DispatchCompute(shader, ZBinningPassUtilityData.KernelLightCount, clearGroups, 1, 1);

                    BindCommon(ZBinningPassUtilityData.KernelLightCount);
                    cmdEncoder.SetComputeIntParam(shader, ZBinningPassUtilityData.ZBin_PhaseID, 1);
                    cmdEncoder.DispatchCompute(shader, ZBinningPassUtilityData.KernelLightCount, lightGroups, 1, 1);

                    BindCommon(ZBinningPassUtilityData.KernelPrefixSum);
                    cmdEncoder.DispatchCompute(shader, ZBinningPassUtilityData.KernelPrefixSum, 1, 1, 1);

                    BindCommon(ZBinningPassUtilityData.KernelFill);
                    cmdEncoder.DispatchCompute(shader, ZBinningPassUtilityData.KernelFill, lightGroups, 1, 1);

                    encoder.SetComputeIntParam(shader, LightShaderIDs.HasTileLightList, 1);
                    encoder.SetGlobalInt("g_HasTileLightList", 1);
                    cmdEncoder.SetGlobalBuffer(ZBinningPassUtilityData.SRV_TileLightRangeID, data.tileRange);
                    cmdEncoder.SetGlobalBuffer(ZBinningPassUtilityData.SRV_TileLightListID, data.tileLightList);
                    cmdEncoder.SetGlobalBuffer(ZBinningPassUtilityData.SRV_ZBinRangeID, data.zBinRange);
                    cmdEncoder.SetGlobalBuffer(ZBinningPassUtilityData.SRV_ZBinLightListID, data.zBinLightList);

                    if (data.overflowBuffer != null)
                    {
                        AsyncGPUReadback.Request(data.overflowBuffer, sizeof(uint), 0, (AsyncGPUReadbackRequest request) =>
                        {
                            if (!request.hasError)
                            {
                                // Diagnostics only — never shrink this-frame ZBin capacity from the readback.
                            }
                        });
                    }
                });
            }
        }
    }
}
