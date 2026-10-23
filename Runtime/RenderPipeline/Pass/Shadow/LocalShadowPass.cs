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
        // Viewport and Texture.Load use matching integer atlas pixel coordinates.
        internal static Vector4 DepthCopyOffset(Vector4 source, Vector4 destination) =>
            new Vector4(source.x - destination.x, source.y - destination.y, 0, 0);

        internal static string TextureName = "LocalShadowMapTexture";
        internal static int LocalShadowMapSizeID = Shader.PropertyToID("_LocalShadowMapSize");
        internal static int LocalShadowCountID = Shader.PropertyToID("_LocalShadowCount");
        internal static int LocalShadowMatricesID = Shader.PropertyToID("_LocalShadowMatrices");
        internal static int LocalShadowParamsID = Shader.PropertyToID("_LocalShadowParams");
        internal static int MatrixViewProjID = Shader.PropertyToID("Matrix_ViewProj");
        internal static readonly int ShadowCasterBiasID = Shader.PropertyToID("_ShadowCasterBias");
        internal static readonly int ShadowCasterLightID = Shader.PropertyToID("_ShadowCasterLight");
    }

    public partial class InfinityRenderPipeline
    {
        struct LocalShadowPassData
        {
            public int lightCount;
            public int sliceCount;
            public int shadowMapResolution;
            public Matrix4x4 cameraViewProj;
            public Matrix4x4 cameraViewFlipYProj;
            public Matrix4x4[] shadowMatrices;
            public Vector4[] tileRects;
            public Vector4[] casterBias, casterLight;
            public RGDrawListRef[] draws;
            public RGRendererListRef[] rendererLists;
        }

        struct CopyCachedLocalShadowPassData
        {
            public int shaderPass;
            public int count;
            public Vector4[] sourceRects, destinationRects;
            public RGTextureRef source;
        }

        sealed class CachedShadowProduction : IRGPassQueueObserver
        {
            public LocalShadowCache cache;
            public FShadowCacheKey[] keys;
            public int count;
            public void OnQueueFailed() { }
            public void OnQueued() => cache.MarkProduced(keys, count);
        }

        // Identity of a cached tile's content: the shadow matrix (position, orientation, projection), caster bias and
        // the caster filters. Scene visibility is part of the exact cache key.
        static int CachedShadowHash(in FLocalShadowSlice slice, Light light, bool excludeStatic)
        {
            var hash = new System.HashCode();
            hash.Add(excludeStatic);
            for (int i = 0; i < 16; ++i) hash.Add(slice.shadowMatrix[i]);
            hash.Add(slice.casterBias);
            hash.Add(slice.atlasPixelRect.z);
            hash.Add(light.cullingMask);
            hash.Add(light.renderingLayerMask);
            return hash.ToHashCode();
        }

        void RasterLocalShadow(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            int shadowMapResolution = pipelineAsset.localShadowMapResolution;
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
            RGTextureRef shadowMapTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.LocalShadowMap, shadowMapDsc);

            // Same tile size as ShadowAllocator.Allocate, so a cached tile copies 1:1 into the frame atlas.
            int tileResolution = math.max(1, shadowMapResolution / 4);


            Matrix4x4[] shadowMatrices = new Matrix4x4[matrixCount];
            Vector4[] tileRects = new Vector4[matrixCount];
            Vector4[] casterBias = new Vector4[matrixCount];
            Vector4[] casterLight = new Vector4[matrixCount];
            RGDrawListRef[] sliceDraws = new RGDrawListRef[matrixCount];
            RGRendererListRef[] sliceRendererLists = new RGRendererListRef[matrixCount];
            // Cache slot per slice (-1 = rendered into the frame atlas) and whether the slot re-renders this frame.
            int[] cacheSlots = new int[matrixCount];
            bool[] cacheRender = new bool[matrixCount];
            FShadowCacheKey[] cacheKeys = new FShadowCacheKey[matrixCount];
            for (int i = 0; i < matrixCount; ++i)
            {
                shadowMatrices[i] = Matrix4x4.identity;
                tileRects[i] = Vector4.zero;
                sliceDraws[i] = RGDrawListRef.Invalid;
                sliceRendererLists[i] = RGRendererListRef.Invalid;
                cacheSlots[i] = -1;
            }

            int lightCount = 0;
            int lastRecord = -1;
            int frame = Time.frameCount;
            long production = 0;
            bool cacheGroupAvailable = false;
            for (int slice = 0; slice < sliceCount; ++slice)
            {
                FLocalShadowSlice local = allocator.LocalSlices[slice];
                shadowMatrices[slice] = local.shadowMatrix;
                tileRects[slice] = local.atlasPixelRect;
                casterBias[slice] = local.casterBias;
                if (local.recordIndex != lastRecord)
                {
                    lastRecord = local.recordIndex;
                    lightCount++;
                    production = m_LocalShadowCache.NextProduction();
                    Light groupLight = cullingResults.visibleLights[local.visibleLightIndex].light;
                    cacheGroupAvailable = m_LocalShadowCache.CanAcquireGroup(UnityEntityId.ToUInt64(groupLight),
                        MeshView.SceneCullingMaskFor(camera), local.isSpot ? 1 : 6, frame);
                }

                if (!local.valid)
                {
                    continue;
                }

                int lightIdx = local.visibleLightIndex;
                VisibleLight visibleLight = cullingResults.visibleLights[lightIdx];
                Light shadowLight = visibleLight.light;
                ulong lightInstanceId = UnityEntityId.ToUInt64(shadowLight);
                Vector3 lightPosition = visibleLight.localToWorldMatrix.GetColumn(3);
                casterLight[slice] = new Vector4(lightPosition.x, lightPosition.y, lightPosition.z, 1);
                uint shadowRenderingLayerMask = RenderingLayerUtility.Validate(unchecked((uint)shadowLight.renderingLayerMask));
                bool excludeStatic = ShadowCasterBounds.ExcludesStatic(shadowLight);

                if (cacheGroupAvailable && shadowLight.TryGetComponent(out InfinityAdditionalLightData ext) && ext.shadowUpdateMode != EShadowUpdateMode.EveryFrame)
                {
                    cacheKeys[slice] = new FShadowCacheKey(lightInstanceId, local.face, MeshView.SceneCullingMaskFor(camera));
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

                Plane[] shadowPlanes = new Plane[local.splitData.cullingPlaneCount];
                for (int plane = 0; plane < shadowPlanes.Length; plane++) shadowPlanes[plane] = local.splitData.GetCullingPlane(plane);
                MeshView localView = MeshView.FromLocalShadow(
                    lightInstanceId,
                    shadowPlanes,
                    lightPosition,
                    shadowLight.cullingMask,
                    shadowRenderingLayerMask,
                    local.face,
                    ShadowCasterBounds.ExcludedFlags(shadowLight),
                    MeshView.SceneCullingMaskFor(camera));
                sliceDraws[slice] = m_RGBuilder.CreateDrawList(localView, MeshPassId.Shadow);

                if (!cullingResults.GetShadowCasterBounds(lightIdx, out _)) continue;
                ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIdx);
                shadowDrawingSettings.useRenderingLayerMaskTest = true;
                shadowDrawingSettings.splitIndex = local.face;
                shadowDrawingSettings.objectsFilter = excludeStatic ? ShadowObjectsFilter.DynamicOnly : ShadowObjectsFilter.AllObjects;
                sliceRendererLists[slice] = m_RGBuilder.CreateShadowRendererList(shadowDrawingSettings);
            }

            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<LocalShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RasterLocalShadow)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(shadowMapTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref LocalShadowPassData passData = ref passRef.GetPassData<LocalShadowPassData>();
                {
                    passData.lightCount = lightCount;
                    passData.sliceCount = sliceCount;
                    passData.shadowMapResolution = shadowMapResolution;
                    passData.cameraViewProj = m_CameraUniform.matrix_ViewProj;
                    passData.cameraViewFlipYProj = m_CameraUniform.matrix_ViewFlipYProj;
                    passData.shadowMatrices = shadowMatrices;
                    passData.tileRects = tileRects;
                    passData.casterBias = casterBias;
                    passData.casterLight = casterLight;
                    passData.draws = new RGDrawListRef[matrixCount];
                    passData.rendererLists = new RGRendererListRef[matrixCount];
                    for (int slice = 0; slice < matrixCount; ++slice)
                    {
                        // Current cached tiles have no draws; they are copied in from the cache afterwards.
                        passData.draws[slice] = sliceDraws[slice].IsValid
                            ? passRef.UseDrawList(sliceDraws[slice])
                            : RGDrawListRef.Invalid;
                        passData.rendererLists[slice] = sliceRendererLists[slice].IsValid
                            ? passRef.UseRendererList(sliceRendererLists[slice])
                            : RGRendererListRef.Invalid;
                    }
                }

                passRef.SetExecuteFunc((in LocalShadowPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetGlobalInt(LocalShadowPassUtilityData.LocalShadowCountID, passData.lightCount);
                    cmdEncoder.SetGlobalVector(LocalShadowPassUtilityData.LocalShadowMapSizeID, new Vector4(
                        passData.shadowMapResolution,
                        passData.shadowMapResolution,
                        1.0f / passData.shadowMapResolution,
                        1.0f / passData.shadowMapResolution));

                    for (int slice = 0; slice < passData.sliceCount; ++slice)
                    {
                        bool hasDraws = passData.draws[slice].IsValid;
                        bool hasRendererList = passData.rendererLists[slice].IsValid;
                        if (!hasDraws && !hasRendererList) continue;
                        string faceMarker = $"LocalShadowSlice{slice}";
                        cmdEncoder.BeginSample(faceMarker);
                        Vector4 rect = passData.tileRects[slice];
                        DrawShadowSlice(cmdEncoder, rect, passData.casterBias[slice], passData.casterLight[slice], passData.shadowMatrices[slice],
                            passData.draws[slice], passData.rendererLists[slice]);
                        cmdEncoder.EndSample(faceMarker);
                    }

                    cmdEncoder.SetGlobalMatrix(LocalShadowPassUtilityData.MatrixViewProjID, passData.cameraViewProj);
                    cmdEncoder.SetGlobalMatrix(CascadeShadowPassUtilityData.MatrixViewFlipYProjID, passData.cameraViewFlipYProj);
                });
            }

            RecordCachedLocalShadows(sliceCount, shadowMapTexture, tileRects, cacheSlots, cacheRender, cacheKeys);
        }

        static void DrawShadowSlice(in RGRasterEncoder cmdEncoder, Vector4 rect, Vector4 casterBias, Vector4 casterLight, Matrix4x4 shadowMatrix,
            RGDrawListRef draws, RGRendererListRef rendererList)
        {
            cmdEncoder.SetViewport(new Rect(rect.x, rect.y, rect.z, rect.w));
            cmdEncoder.SetGlobalDepthBias(0, 0);
            cmdEncoder.SetGlobalVector(LocalShadowPassUtilityData.ShadowCasterBiasID, casterBias);
            cmdEncoder.SetGlobalVector(LocalShadowPassUtilityData.ShadowCasterLightID, casterLight);
            cmdEncoder.SetGlobalMatrix(LocalShadowPassUtilityData.MatrixViewProjID, shadowMatrix);
            cmdEncoder.SetGlobalMatrix(CascadeShadowPassUtilityData.MatrixViewFlipYProjID, shadowMatrix);
            if (draws.IsValid) cmdEncoder.Draw(draws);
            if (rendererList.IsValid) cmdEncoder.DrawRendererList(rendererList);
            cmdEncoder.SetGlobalDepthBias(0.0f, 0.0f);
        }

        // Cached slices. A stale tile is drawn into its (cleared) frame-atlas tile like any other slice and then saved
        // into its cache slot; a current tile is loaded from its cache slot. Drawing into the frame atlas avoids a
        // partial depth clear of the persistent atlas. Saving is the cache production: the slot commits when this
        // pass is queued and the frame is submitted.
        void RecordCachedLocalShadows(int sliceCount, RGTextureRef shadowMapTexture, Vector4[] tileRects, int[] cacheSlots,
            bool[] cacheRender, FShadowCacheKey[] cacheKeys)
        {
            int saveCount = 0, loadCount = 0;
            for (int slice = 0; slice < sliceCount; ++slice)
            {
                if (cacheSlots[slice] < 0) continue;
                if (cacheRender[slice]) saveCount++;
                else loadCount++;
            }
            InfinityDebugDisplaySettings.current.lighting.lastCachedShadowTiles = saveCount + loadCount;
            InfinityDebugDisplaySettings.current.lighting.lastCachedShadowRenders = saveCount;
            if (saveCount + loadCount == 0)
            {
                return;
            }

            RGTextureRef cacheAtlas = m_RGBuilder.ImportTexture(m_LocalShadowCache.GetAtlas());
            m_RGScoper.RegisterTexture(InfinityShaderIDs.CachedLocalShadowMap, cacheAtlas);

            int shaderPass = GraphicsUtility.BlitMaterial.FindPass("RasterCopyShadowDepthTile");
            if (shaderPass < 0) throw new System.InvalidOperationException("InfinityRP: required CopyShadowDepthTile shader pass is missing.");
            var frameRects = new Vector4[saveCount + loadCount];
            var cacheRects = new Vector4[saveCount + loadCount];
            var keys = new FShadowCacheKey[saveCount];
            int save = 0, load = saveCount;
            for (int slice = 0; slice < sliceCount; ++slice)
            {
                if (cacheSlots[slice] < 0) continue;
                int write = cacheRender[slice] ? save++ : load++;
                if (cacheRender[slice]) keys[write] = cacheKeys[slice];
                frameRects[write] = tileRects[slice];
                cacheRects[write] = m_LocalShadowCache.SlotPixelRect(cacheSlots[slice]);
            }
            // Depth-only resources cannot use region CopyTexture. Keep each direction in its own raster pass:
            // sampling and attaching the same physical atlas in one pass would be a feedback hazard.
            if (saveCount > 0)
                RecordShadowDepthCopies(shadowMapTexture, cacheAtlas, frameRects, cacheRects, 0, saveCount, shaderPass,
                    new CachedShadowProduction { cache = m_LocalShadowCache, keys = keys, count = saveCount });
            if (loadCount > 0)
                RecordShadowDepthCopies(cacheAtlas, shadowMapTexture, cacheRects, frameRects, saveCount, loadCount, shaderPass, null);
        }

        void RecordShadowDepthCopies(RGTextureRef source, RGTextureRef destination, Vector4[] sourceRects, Vector4[] destinationRects,
            int first, int count, int shaderPass, IRGPassQueueObserver observer)
        {
            using (var pass = m_RGBuilder.AddRasterPass<CopyCachedLocalShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RasterCopyShadowDepthTiles)))
            {
                pass.SetDepthStencilAttachment(destination, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);
                ref var data = ref pass.GetPassData<CopyCachedLocalShadowPassData>();
                data.source = pass.ReadTexture(source);
                data.shaderPass = shaderPass;
                data.count = count;
                data.sourceRects = new Vector4[count];
                data.destinationRects = new Vector4[count];
                System.Array.Copy(sourceRects, first, data.sourceRects, 0, count);
                System.Array.Copy(destinationRects, first, data.destinationRects, 0, count);
                if (observer != null) pass.SetQueueObserver(observer);
                pass.SetExecuteFunc((in CopyCachedLocalShadowPassData data, in RGRasterEncoder commands, RGObjectPool pool) =>
                {
                    commands.SetGlobalTexture(Shader.PropertyToID("_InfinitySourceDepth"), data.source);
                    for (int i = 0; i < data.count; ++i)
                    {
                        Vector4 rect = data.destinationRects[i];
                        commands.SetViewport(new Rect(rect.x, rect.y, rect.z, rect.w));
                        commands.SetGlobalVector(Shader.PropertyToID("_InfinityDepthCopyOffset"),
                            LocalShadowPassUtilityData.DepthCopyOffset(data.sourceRects[i], rect));
                        commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, data.shaderPass);
                    }
                });
            }
        }
    }
}
