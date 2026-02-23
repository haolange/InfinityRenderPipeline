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
        internal static string ZBinBufferName = "ZBinLightListBuffer";
        internal static string TileBufferName = "TileLightListBuffer";
        internal static int ZBin_ScreenSizeID = Shader.PropertyToID("ZBin_ScreenSize");
        internal static int ZBin_TileSizeID = Shader.PropertyToID("ZBin_TileSize");
        internal static int ZBin_NumTilesID = Shader.PropertyToID("ZBin_NumTiles");
        internal static int ZBin_NearFarID = Shader.PropertyToID("ZBin_NearFar");
        internal static int ZBin_NumBinsID = Shader.PropertyToID("ZBin_NumBins");
        internal static int ZBin_LightCountID = Shader.PropertyToID("ZBin_LightCount");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int UAV_ZBinBufferID = Shader.PropertyToID("UAV_ZBinBuffer");
        internal static int UAV_TileLightListID = Shader.PropertyToID("UAV_TileLightList");
        internal static int SRV_LightBoundsBufferID = Shader.PropertyToID("SRV_LightBoundsBuffer");
        internal static int KernelZBinning = 0;
        internal static int KernelTileLighting = 1;
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
            public float nearPlane;
            public float farPlane;
            public ComputeShader zBinningShader;
            public RGTextureRef depthTexture;
            public RGBufferRef zBinBuffer;
            public RGBufferRef tileLightListBuffer;
        }

        void ComputeZBinningLightList(RenderContext renderContext, Camera camera)
        {
            int tileSize = 16;
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            int numTilesX = Mathf.CeilToInt((float)width / tileSize);
            int numTilesY = Mathf.CeilToInt((float)height / tileSize);
            int numBins = 256;
            int maxLightsPerTile = 64;

            BufferDescriptor zBinBufferDsc = new BufferDescriptor();
            {
                zBinBufferDsc.name = ZBinningPassUtilityData.ZBinBufferName;
                zBinBufferDsc.count = numBins * 2;
                zBinBufferDsc.stride = sizeof(uint);
                zBinBufferDsc.type = ComputeBufferType.Structured;
            }
            RGBufferRef zBinBuffer = m_RGScoper.CreateBuffer(InfinityShaderIDs.ZBinLightListBuffer, zBinBufferDsc);

            BufferDescriptor tileBufferDsc = new BufferDescriptor();
            {
                tileBufferDsc.name = ZBinningPassUtilityData.TileBufferName;
                tileBufferDsc.count = numTilesX * numTilesY * maxLightsPerTile;
                tileBufferDsc.stride = sizeof(uint);
                tileBufferDsc.type = ComputeBufferType.Structured;
            }
            RGBufferRef tileLightListBuffer = m_RGScoper.CreateBuffer(InfinityShaderIDs.TileLightListBuffer, tileBufferDsc);

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            //Add ZBinningPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<ZBinningPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeZBinningLightList)))
            {
                //Setup Phase
                ref ZBinningPassData passData = ref passRef.GetPassData<ZBinningPassData>();
                passData.tileSize = tileSize;
                passData.screenSize = new int2(width, height);
                passData.numTiles = new int2(numTilesX, numTilesY);
                passData.numBins = numBins;
                passData.lightCount = m_LightContext != null ? 0 : 0; // Will be set from LightContext
                passData.nearPlane = camera.nearClipPlane;
                passData.farPlane = camera.farClipPlane;
                passData.zBinningShader = pipelineAsset.zBinningShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.zBinBuffer = passRef.WriteBuffer(zBinBuffer);
                passData.tileLightListBuffer = passRef.WriteBuffer(tileLightListBuffer);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(false);
                passRef.SetExecuteFunc((in ZBinningPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.zBinningShader == null) return;

                    // Z-Binning pass: assign light ranges to depth bins
                    cmdEncoder.SetComputeVectorParam(passData.zBinningShader, ZBinningPassUtilityData.ZBin_ScreenSizeID, new Vector4(passData.screenSize.x, passData.screenSize.y, 1.0f / passData.screenSize.x, 1.0f / passData.screenSize.y));
                    cmdEncoder.SetComputeIntParam(passData.zBinningShader, ZBinningPassUtilityData.ZBin_TileSizeID, passData.tileSize);
                    cmdEncoder.SetComputeVectorParam(passData.zBinningShader, ZBinningPassUtilityData.ZBin_NumTilesID, new Vector4(passData.numTiles.x, passData.numTiles.y, 0, 0));
                    cmdEncoder.SetComputeVectorParam(passData.zBinningShader, ZBinningPassUtilityData.ZBin_NearFarID, new Vector4(passData.nearPlane, passData.farPlane, 0, 0));
                    cmdEncoder.SetComputeIntParam(passData.zBinningShader, ZBinningPassUtilityData.ZBin_NumBinsID, passData.numBins);
                    cmdEncoder.SetComputeIntParam(passData.zBinningShader, ZBinningPassUtilityData.ZBin_LightCountID, passData.lightCount);

                    cmdEncoder.SetComputeTextureParam(passData.zBinningShader, ZBinningPassUtilityData.KernelZBinning, ZBinningPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeBufferParam(passData.zBinningShader, ZBinningPassUtilityData.KernelZBinning, ZBinningPassUtilityData.UAV_ZBinBufferID, passData.zBinBuffer);
                    cmdEncoder.DispatchCompute(passData.zBinningShader, ZBinningPassUtilityData.KernelZBinning, Mathf.CeilToInt(passData.numBins / 64.0f), 1, 1);

                    // Tile light list pass: build per-tile light lists using wave ops
                    cmdEncoder.SetComputeBufferParam(passData.zBinningShader, ZBinningPassUtilityData.KernelTileLighting, ZBinningPassUtilityData.UAV_TileLightListID, passData.tileLightListBuffer);
                    cmdEncoder.SetComputeTextureParam(passData.zBinningShader, ZBinningPassUtilityData.KernelTileLighting, ZBinningPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.DispatchCompute(passData.zBinningShader, ZBinningPassUtilityData.KernelTileLighting, passData.numTiles.x, passData.numTiles.y, 1);
                });
            }
        }
    }
}
