using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Component;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.LightPipeline;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class LocalShadowPassUtilityData
    {
        internal static readonly int InfinityDepthCopyOffsetID = Shader.PropertyToID("_InfinityDepthCopyOffset");
        // Viewport and Texture.Load use matching integer atlas pixel coordinates.
        internal static Vector4 DepthCopyOffset(Vector4 source, Vector4 destination) =>
            new Vector4(source.x - destination.x, source.y - destination.y, 0, 0);

        internal static readonly string TextureName = "LocalShadowMapTexture";
        internal static readonly int LocalShadowMatricesID = Shader.PropertyToID("_LocalShadowMatrices");
        internal static readonly int LocalShadowParamsID = Shader.PropertyToID("_LocalShadowParams");
    }

    public partial class InfinityRenderPipeline
    {
        struct LocalShadowPassData
        {
            public int sliceCount;
            public Matrix4x4[] shadowMatrices;
            public Vector4[] tileRects;
            public Vector4[] casterBias, casterLight;
            public RGMeshDrawListHandle[] meshDrawListsHandle;
            public RGRendererListHandle[] rendererListsHandle;
        }

        struct CopyCachedLocalShadowPassData
        {
            public int shaderPass;
            public int count;
            public Vector4[] sourceRects, destinationRects;
            public RGTextureHandle sourceHandle;
        }

        // One observer for the pipeline's lifetime: the graph that queues it is executed and cleared before the next
        // camera records and rewrites its fields.
        readonly CachedShadowProduction m_CachedShadowProduction = new CachedShadowProduction();

        sealed class CachedShadowProduction : IRGPassQueueObserver
        {
            public LocalShadowCache cache;
            public ShadowCacheKey[] keys;
            public int count;
            public void OnQueueFailed() { }
            public void OnQueued() => cache.MarkProduced(keys, count);
        }

        // Identity of a cached tile's content: the shadow matrix (position, orientation, projection), caster bias and
        // the caster filters. Scene visibility is part of the exact cache key.
        static int CachedShadowHash(in LocalShadowSlice slice, Light light, bool excludeStatic)
        {
            var hash = new System.HashCode();
            hash.Add(excludeStatic);
            for (int i = 0; i < 16; ++i)
            {
                hash.Add(slice.shadowMatrix[i]);
            }
            hash.Add(slice.casterBias);
            hash.Add(slice.atlasPixelRect.z);
            hash.Add(light.cullingMask);
            hash.Add(light.renderingLayerMask);
            return hash.ToHashCode();
        }

        void RasterLocalShadow(in CameraRecordContext context)
        {
            int shadowMapResolution = m_Settings.localShadowMapResolution;
            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            int sliceCount = allocator.LocalSliceCount;
            int matrixCount = math.max(1, sliceCount);

            TextureDescriptor shadowMapDsc = new TextureDescriptor(shadowMapResolution, shadowMapResolution);
            {
                shadowMapDsc.name = LocalShadowPassUtilityData.TextureName;
                shadowMapDsc.dimension = TextureDimension.Tex2D;
                shadowMapDsc.colorFormat = GraphicsFormat.None;
                shadowMapDsc.depthBufferBits = EDepthBits.Depth32;
                shadowMapDsc.isShadowMap = true;
                shadowMapDsc.filterMode = FilterMode.Bilinear;
            }
            RGTextureHandle shadowMapTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.LocalShadowMap, shadowMapDsc);

            // Same tile size as ShadowAllocator.Allocate, so a cached tile copies 1:1 into the frame atlas.
            int tileResolution = math.max(1, shadowMapResolution / 4);


            Matrix4x4[] shadowMatrices = m_RGBuilder.GetRecordingArray<Matrix4x4>(matrixCount);
            Vector4[] tileRects = m_RGBuilder.GetRecordingArray<Vector4>(matrixCount);
            Vector4[] casterBias = m_RGBuilder.GetRecordingArray<Vector4>(matrixCount);
            Vector4[] casterLight = m_RGBuilder.GetRecordingArray<Vector4>(matrixCount);
            RGMeshDrawListHandle[] sliceMeshDrawListsHandle = m_RGBuilder.GetRecordingArray<RGMeshDrawListHandle>(matrixCount);
            RGRendererListHandle[] sliceRendererListsHandle = m_RGBuilder.GetRecordingArray<RGRendererListHandle>(matrixCount);
            // Cache slot per slice (-1 = rendered into the frame atlas) and whether the slot re-renders this frame.
            int[] cacheSlots = m_RGBuilder.GetRecordingArray<int>(matrixCount);
            bool[] cacheRender = m_RGBuilder.GetRecordingArray<bool>(matrixCount);
            ShadowCacheKey[] cacheKeys = m_RGBuilder.GetRecordingArray<ShadowCacheKey>(matrixCount);
            for (int i = 0; i < matrixCount; ++i)
            {
                shadowMatrices[i] = Matrix4x4.identity;
                tileRects[i] = Vector4.zero;
                sliceMeshDrawListsHandle[i] = RGMeshDrawListHandle.InvalidHandle;
                sliceRendererListsHandle[i] = RGRendererListHandle.InvalidHandle;
                cacheSlots[i] = -1;
            }

            int lastRecord = -1;
            int frame = Time.frameCount;
            long production = 0;
            bool cacheGroupAvailable = false;
            for (int slice = 0; slice < sliceCount; ++slice)
            {
                LocalShadowSlice local = allocator.LocalSlices[slice];
                shadowMatrices[slice] = local.shadowMatrix;
                tileRects[slice] = local.atlasPixelRect;
                casterBias[slice] = local.casterBias;
                if (local.recordIndex != lastRecord)
                {
                    lastRecord = local.recordIndex;
                    production = m_LocalShadowCache.NextProduction();
                    Light groupLight = context.cullingResults.visibleLights[local.visibleLightIndex].light;
                    cacheGroupAvailable = m_LocalShadowCache.CanAcquireGroup(UnityEntityId.ToUInt64(groupLight),
                        CameraSceneCulling.Mask(context.camera), local.isSpot ? 1 : 6, frame);
                }

                if (!local.valid)
                {
                    continue;
                }

                int lightIdx = local.visibleLightIndex;
                VisibleLight visibleLight = context.cullingResults.visibleLights[lightIdx];
                Light shadowLight = visibleLight.light;
                ulong lightInstanceId = UnityEntityId.ToUInt64(shadowLight);
                Vector3 lightPosition = visibleLight.localToWorldMatrix.GetColumn(3);
                casterLight[slice] = new Vector4(lightPosition.x, lightPosition.y, lightPosition.z, 1);
                bool excludeStatic = ShadowCasterBounds.ExcludesStatic(shadowLight);

                InfinityAdditionalLightData ext = renderContext.lightContext.AdditionalData(lightIdx);
                if (cacheGroupAvailable && ext != null && ext.shadowUpdateMode != EShadowUpdateMode.EveryFrame)
                {
                    cacheKeys[slice] = new ShadowCacheKey(lightInstanceId, local.face, CameraSceneCulling.Mask(context.camera));
                    cacheSlots[slice] = m_LocalShadowCache.Acquire(cacheKeys[slice], ext.shadowUpdateMode,
                        CachedShadowHash(local, shadowLight, excludeStatic), ext.shadowCacheVersion, frame, out cacheRender[slice]);
                    if (cacheSlots[slice] >= 0 && cacheRender[slice])
                        m_LocalShadowCache.SetPendingSampling(cacheKeys[slice], new LocalShadowCache.SamplingState
                        {
                            slice = local, gpu = allocator.LocalGPU[slice],
                            faceCount = local.isSpot ? 1 : 6, production = production
                        });
                    if (cacheSlots[slice] >= 0 && !cacheRender[slice])
                    {
                        // The cached tile is current: no casters are drawn for it this frame.
                        continue;
                    }
                }

                sliceMeshDrawListsHandle[slice] = m_RGBuilder.CreateMeshDrawList(new MeshDrawListDesc(
                    CameraCulling.Context(context.camera, shadowLight, local.splitData), InfinityPassIDs.RasterShadowPass));

                if (!context.cullingResults.GetShadowCasterBounds(lightIdx, out _))
                {
                    continue;
                }
                ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(context.cullingResults, lightIdx);
                shadowDrawingSettings.useRenderingLayerMaskTest = true;
                shadowDrawingSettings.splitIndex = local.face;
                shadowDrawingSettings.objectsFilter = excludeStatic ? ShadowObjectsFilter.DynamicOnly : ShadowObjectsFilter.AllObjects;
                sliceRendererListsHandle[slice] = m_RGBuilder.CreateShadowRendererList(shadowDrawingSettings);
            }

            {
                RGRasterPassHandle<LocalShadowPassData> passHandle = m_RGBuilder.AddRasterPass<LocalShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RasterLocalShadow));
                passHandle.EnablePassCulling(false);
                passHandle.SetDepthStencilAttachment(shadowMapTextureHandle, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref LocalShadowPassData passData = ref passHandle.GetPassData();
                {
                    passData.sliceCount = sliceCount;
                    passData.shadowMatrices = shadowMatrices;
                    passData.tileRects = tileRects;
                    passData.casterBias = casterBias;
                    passData.casterLight = casterLight;
                    passData.meshDrawListsHandle = m_RGBuilder.GetRecordingArray<RGMeshDrawListHandle>(matrixCount);
                    passData.rendererListsHandle = m_RGBuilder.GetRecordingArray<RGRendererListHandle>(matrixCount);
                    for (int slice = 0; slice < matrixCount; ++slice)
                    {
                        // Current cached tiles have no draws; they are copied in from the cache afterwards.
                        passData.meshDrawListsHandle[slice] = sliceMeshDrawListsHandle[slice].IsValid
                            ? passHandle.UseMeshDrawList(sliceMeshDrawListsHandle[slice])
                            : RGMeshDrawListHandle.InvalidHandle;
                        passData.rendererListsHandle[slice] = sliceRendererListsHandle[slice].IsValid
                            ? passHandle.UseRendererList(sliceRendererListsHandle[slice])
                            : RGRendererListHandle.InvalidHandle;
                    }
                }

                passHandle.SetExecuteFunc((in LocalShadowPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    for (int slice = 0; slice < passData.sliceCount; ++slice)
                    {
                        bool hasDraws = passData.meshDrawListsHandle[slice].IsValid;
                        bool hasRendererList = passData.rendererListsHandle[slice].IsValid;
                        if (!hasDraws && !hasRendererList)
                        {
                            continue;
                        }
                        ProfilingSampler faceMarker = MeshDrawProfiling.SliceSampler(slice);
                        cmdEncoder.BeginSample(faceMarker);
                        Vector4 rect = passData.tileRects[slice];
                        DrawShadowSlice(cmdEncoder, rect, passData.casterBias[slice], passData.casterLight[slice], passData.shadowMatrices[slice],
                            passData.meshDrawListsHandle[slice], passData.rendererListsHandle[slice]);
                        cmdEncoder.EndSample(faceMarker);
                    }
                });
            }

            RecordCachedLocalShadows(sliceCount, shadowMapTextureHandle, tileRects, cacheSlots, cacheRender, cacheKeys);
        }

        static void DrawShadowSlice(in RGRasterEncoder cmdEncoder, Vector4 rect, Vector4 casterBias, Vector4 casterLight, Matrix4x4 shadowMatrix,
            RGMeshDrawListHandle drawsHandle, RGRendererListHandle rendererListHandle)
        {
            cmdEncoder.SetViewport(new Rect(rect.x, rect.y, rect.z, rect.w));
            cmdEncoder.SetGlobalDepthBias(0, 0);
            cmdEncoder.SetGlobalVector(PassShaderIDs.ShadowCasterBias, casterBias);
            cmdEncoder.SetGlobalVector(PassShaderIDs.ShadowCasterLight, casterLight);
            cmdEncoder.SetGlobalMatrix(PassShaderIDs.ShadowCasterViewProj, shadowMatrix);
            if (drawsHandle.IsValid)
            {
                cmdEncoder.SubmitMeshDrawList(drawsHandle);
            }
            if (rendererListHandle.IsValid)
            {
                cmdEncoder.DrawRendererList(rendererListHandle);
            }
            cmdEncoder.SetGlobalDepthBias(0.0f, 0.0f);
        }

        // Cached slices. A stale tile is drawn into its (cleared) frame-atlas tile like any other slice and then saved
        // into its cache slot; a current tile is loaded from its cache slot. Drawing into the frame atlas avoids a
        // partial depth clear of the persistent atlas. Saving is the cache production: the slot commits when this
        // pass is queued and the frame is submitted.
        void RecordCachedLocalShadows(int sliceCount, RGTextureHandle shadowMapTextureHandle, Vector4[] tileRects, int[] cacheSlots,
            bool[] cacheRender, ShadowCacheKey[] cacheKeys)
        {
            int saveCount = 0, loadCount = 0;
            for (int slice = 0; slice < sliceCount; ++slice)
            {
                if (cacheSlots[slice] < 0)
                {
                    continue;
                }
                if (cacheRender[slice])
                {
                    saveCount++;
                }
                else
                {
                    loadCount++;
                }
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            InfinityDebugDisplaySettings.current.lighting.lastCachedShadowTiles = saveCount + loadCount;
            InfinityDebugDisplaySettings.current.lighting.lastCachedShadowRenders = saveCount;
#endif
            if (saveCount + loadCount == 0)
            {
                return;
            }

            RGTextureHandle cacheAtlasHandle = m_RGBuilder.ImportTexture(m_LocalShadowCache.GetAtlas());
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.CachedLocalShadowMap, cacheAtlasHandle);

            int shaderPass = blitPasses.copyShadowDepthTile;
            var frameRects = m_RGBuilder.GetRecordingArray<Vector4>(saveCount + loadCount);
            var cacheRects = m_RGBuilder.GetRecordingArray<Vector4>(saveCount + loadCount);
            var keys = m_RGBuilder.GetRecordingArray<ShadowCacheKey>(saveCount);
            int save = 0, load = saveCount;
            for (int slice = 0; slice < sliceCount; ++slice)
            {
                if (cacheSlots[slice] < 0)
                {
                    continue;
                }
                int write = cacheRender[slice] ? save++ : load++;
                if (cacheRender[slice])
                {
                    keys[write] = cacheKeys[slice];
                }
                frameRects[write] = tileRects[slice];
                cacheRects[write] = m_LocalShadowCache.SlotPixelRect(cacheSlots[slice]);
            }
            // Depth-only resources cannot use region CopyTexture. Keep each direction in its own raster pass:
            // sampling and attaching the same physical atlas in one pass would be a feedback hazard.
            if (saveCount > 0)
            {
                m_CachedShadowProduction.cache = m_LocalShadowCache;
                m_CachedShadowProduction.keys = keys;
                m_CachedShadowProduction.count = saveCount;
                RecordShadowDepthCopies(shadowMapTextureHandle, cacheAtlasHandle, frameRects, cacheRects, 0, saveCount, shaderPass, m_CachedShadowProduction);
            }
            if (loadCount > 0)
                RecordShadowDepthCopies(cacheAtlasHandle, shadowMapTextureHandle, cacheRects, frameRects, saveCount, loadCount, shaderPass, null);
        }

        void RecordShadowDepthCopies(RGTextureHandle sourceHandle, RGTextureHandle destinationHandle, Vector4[] sourceRects, Vector4[] destinationRects,
            int first, int count, int shaderPass, IRGPassQueueObserver observer)
        {
            {
                RGRasterPassHandle<CopyCachedLocalShadowPassData> passHandle = m_RGBuilder.AddRasterPass<CopyCachedLocalShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RasterCopyShadowDepthTiles));
                passHandle.SetDepthStencilAttachment(destinationHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);
                ref CopyCachedLocalShadowPassData passData = ref passHandle.GetPassData();
                passData.sourceHandle = passHandle.ReadTexture(sourceHandle);
                passData.shaderPass = shaderPass;
                passData.count = count;
                passData.sourceRects = m_RGBuilder.GetRecordingArray<Vector4>(count);
                passData.destinationRects = m_RGBuilder.GetRecordingArray<Vector4>(count);
                System.Array.Copy(sourceRects, first, passData.sourceRects, 0, count);
                System.Array.Copy(destinationRects, first, passData.destinationRects, 0, count);
                if (observer != null)
                {
                    passHandle.SetQueueObserver(observer);
                }
                passHandle.SetExecuteFunc((in CopyCachedLocalShadowPassData passData, in RGRasterEncoder commands) =>
                {
                    commands.SetGlobalTexture(PassShaderIDs.InfinitySourceDepth, passData.sourceHandle);
                    for (int i = 0; i < passData.count; ++i)
                    {
                        Vector4 rect = passData.destinationRects[i];
                        commands.SetViewport(new Rect(rect.x, rect.y, rect.z, rect.w));
                        commands.SetGlobalVector(LocalShadowPassUtilityData.InfinityDepthCopyOffsetID,
                            LocalShadowPassUtilityData.DepthCopyOffset(passData.sourceRects[i], rect));
                        commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, passData.shaderPass);
                    }
                });
            }
        }
    }
}
