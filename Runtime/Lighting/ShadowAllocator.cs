using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Component;
using InfinityTech.Core;

namespace InfinityTech.Rendering.LightPipeline
{
    internal struct ShadowAllocatorSettings
    {
        public int cascadeMapResolution;
        public int localMapResolution;
        public float shadowDistance;
        public Vector3 cascadeRatios;
        public int maxLocalLights;
        public int cascadeCount;
        public float cascadeBlendWidth;
        // Texels of filter footprint kept inside each local tile frustum, so filtering never reads past a cube face.
        public int filterGuardTexels;
    }

    internal struct LocalShadowSlice
    {
        public int visibleLightIndex;
        public int recordIndex;
        public int face;
        public int matrixIndex;
        public bool isSpot;
        public bool cachedSampling;
        public BatchCullingProjectionType projectionType;
        public Vector4 atlasPixelRect;
        public Vector4 atlasUVRect;
        public Matrix4x4 shadowMatrix;
        public Vector4 casterBias;
        public ShadowSplitData splitData;
        // EShadowFilter of the owning record; VSM / EVSM slices get moments.
        public int shadowFilter;
        public float far;
        public bool valid;
    }

    internal static class ShadowPCSSShapeMode
    {
        public const float IsotropicDisk = 0.0f;
        public const float OrientedRect = 1.0f;
        public const float OrientedCapsule = 2.0f;
    }

    // Per local shadow slice data the shaders read (SRV_LocalShadowRects).
    // GPU-RECORD: LocalShadowSlice Shaders/Library/ShadowSampling.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct LocalShadowSliceGPU
    {
        // xy = atlas UV origin, zw = atlas UV size.
        public Vector4 atlasRect;
        // x, y = m22, m23 of the GPU projection (linear depth from stored depth); z = world texel size at unit depth
        // (perspective) or world texel size (orthographic); w = 1 for orthographic.
        public Vector4 depthParams;
        // x = 1 / far: normalizes linear depth for VSM / EVSM moments; y = virtual-apex offset behind the emitter plane.
        public Vector4 momentParams;
        // xyz = origin used to select cube faces; w = PCSS effective emitter size (enables Line/Tube/Rect/Disk/Sphere).
        public Vector4 shadowOriginSize;
        // xy = unit slice view-XY direction of emitter U axis; z = half-extent along U; w = ShadowPCSSShapeMode.
        public Vector4 emitterAxisU;
        // xy = unit slice view-XY direction of emitter V axis; z = half-extent along V; w = |m11| / max(|m00|, 1e-6).
        public Vector4 emitterAxisV;
    }

    internal struct CascadeShadowSlice
    {
        public int cullingSplitIndex;
        public Matrix4x4 cullingMatrix;
        public Matrix4x4 shadowMatrix;
        public Vector4 atlasPixelRect;
        public Vector4 atlasUVRect;
        public Vector4 casterBias;
        public ShadowSplitData splitData;
        public bool valid;
    }

    /// <summary>
    /// Cascades: up to 4 tiles in a 2x2 atlas for the first shadowed directional light. Local atlas: point, sphere,
    /// line and tube lights take 6 cube faces; cone, pyramid and box spots, rects and disks one tile. Every local
    /// frustum is built here (not by Unity's light-type helpers) so every shape shadows the same way, and its culling
    /// planes go to CullShadowCasters. Writes atlas / matrix indices back into LightRecord before the GPU upload.
    /// </summary>
    internal sealed partial class ShadowAllocator
    {
        public const int CascadeCount = 4;
        internal static readonly Vector3 DefaultCascadeRatios = new Vector3(0.067f, 0.2f, 0.467f);
        // Rect and disk shadows cover a 120 degree cone in front of the emitter.
        internal const float PlanarShadowFieldOfView = 120.0f;

        // Split ratios must be strictly increasing inside (0, 1). The RP Asset keeps them ordered in OnValidate;
        // a script that writes an invalid value fails at record time instead of rendering unordered cascades.
        internal static Vector3 ValidateCascadeRatios(Vector3 ratios)
        {
            bool valid = ratios.x > 0.0f && ratios.y > ratios.x && ratios.z > ratios.y && ratios.z < 1.0f;
            if (!valid)
                throw new ArgumentOutOfRangeException(nameof(ratios), $"InfinityRP: cascade split ratios {ratios} must be strictly increasing inside (0, 1).");
            return ratios;
        }

        public int CascadeVisibleLightIndex { get; private set; } = -1;
        public int CascadeRecordIndex { get; private set; } = -1;
        public int CascadeAllocatedCount
        {
            get;
            private set;
        }
        // Cascades requested by the asset (1-4); slices past it stay invalid.
        public int ActiveCascadeCount { get; private set; } = CascadeCount;
        public Vector4 CascadeSplitDistances
        {
            get;
            private set;
        }
        public readonly CascadeShadowSlice[] CascadeSlices = new CascadeShadowSlice[CascadeCount];
        public readonly Matrix4x4[] CascadeMatrices = new Matrix4x4[CascadeCount];
        public readonly Vector4[] CascadeSpheres = new Vector4[CascadeCount];
        // x = world texel size, y, z = m22, m23 of the GPU projection, w = blend width (fraction of the cascade).
        public readonly Vector4[] CascadeParams = new Vector4[CascadeCount];

        // Shadow filter of the cascaded light (EShadowFilter), -1 without one.
        public int CascadeShadowFilter { get; private set; } = -1;
        public bool CascadeUsesMoments => CascadeAllocatedCount > 0 && UsesMoments(CascadeShadowFilter);
        // Valid local slices whose light filters with VSM / EVSM.
        public int LocalMomentSliceCount
        {
            get;
            private set;
        }

        public static bool UsesMoments(int shadowFilter) => shadowFilter == (int)EShadowFilter.VSM || shadowFilter == (int)EShadowFilter.EVSM;

        public int LocalSliceCount
        {
            get;
            private set;
        }
        public int LocalAtlasResolution { get; private set; }
        public int LocalMinTileResolution { get; private set; }
        public int LocalMaxTileResolution { get; private set; }
        public float LocalAtlasOccupancy { get; private set; }
        public int LocalDowngradedLightCount { get; private set; }
        public int LocalDroppedLightCount { get; private set; }
        public int LocalTileCount1024 { get; private set; }
        public int LocalTileCount512 { get; private set; }
        public int LocalTileCount256 { get; private set; }
        public int LocalTileCount128OrSmaller { get; private set; }

        readonly ShadowAtlasLayout m_FrameLayout = new ShadowAtlasLayout();
        readonly ShadowResolutionHistory m_ResolutionHistory = new ShadowResolutionHistory();
        public LocalShadowSlice[] LocalSlices = Array.Empty<LocalShadowSlice>();
        public Matrix4x4[] LocalMatrices = Array.Empty<Matrix4x4>();
        public LocalShadowSliceGPU[] LocalGPU = Array.Empty<LocalShadowSliceGPU>();

        public void Reset()
        {
            CascadeVisibleLightIndex = -1;
            CascadeRecordIndex = -1;
            CascadeAllocatedCount = 0;
            CascadeSplitDistances = Vector4.zero;
            CascadeShadowFilter = -1;
            LocalSliceCount = 0;
            LocalMomentSliceCount = 0;
            LocalAtlasOccupancy = 0f;
            LocalDowngradedLightCount = 0;
            LocalDroppedLightCount = 0;
            LocalTileCount1024 = 0;
            LocalTileCount512 = 0;
            LocalTileCount256 = 0;
            LocalTileCount128OrSmaller = 0;
            m_FrameLayout.Reset();
            for (int i = 0; i < CascadeCount; ++i)
            {
                CascadeSlices[i] = default;
                CascadeMatrices[i] = Matrix4x4.identity;
                CascadeSpheres[i] = Vector4.zero;
                CascadeParams[i] = Vector4.zero;
            }
        }

        internal static int ResolveLocalAtlasResolution(int requestedResolution)
        {
            return ShadowAtlasLayout.PreviousPowerOfTwo(math.max(64, requestedResolution));
        }

        internal static void ResolveLocalResolutionBounds(int localRes, out int minTileResolution, out int maxTileResolution)
        {
            int clampedAtlas = ResolveLocalAtlasResolution(localRes);
            int floorBound = Mathf.Min(64, clampedAtlas);
            minTileResolution = ShadowAtlasLayout.ClampPowerOfTwo(Mathf.Max(floorBound, clampedAtlas / 16), floorBound, Mathf.Max(floorBound, clampedAtlas / 4));
            maxTileResolution = ShadowAtlasLayout.ClampPowerOfTwo(Mathf.Max(minTileResolution, clampedAtlas / 2), minTileResolution, clampedAtlas);
        }

        public void Allocate(
            NativeList<LightRecord> records,
            in CullingResults cullingResults,
            InfinityAdditionalLightData[] additionalData,
            Camera camera,
            in ShadowAllocatorSettings settings, MeshWorld meshWorld, MeshPassId shadowPass, int frame, LocalShadowCache shadowCache = null)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }
            if (meshWorld == null)
            {
                throw new ArgumentNullException(nameof(meshWorld));
            }
            using (LightCpuMarkers.s_CollectCasters.Auto())
            {
                m_MeshCasters.Collect(meshWorld, shadowPass, frame);
            }
            Reset();

            int cascadeRes = math.max(1, settings.cascadeMapResolution);
            int localRes = ResolveLocalAtlasResolution(settings.localMapResolution);
            ResolveLocalResolutionBounds(localRes, out int minTileResolution, out int maxTileResolution);
            LocalAtlasResolution = localRes;
            LocalMinTileResolution = minTileResolution;
            LocalMaxTileResolution = maxTileResolution;
            m_FrameLayout.Configure(localRes, localRes, minTileResolution, maxTileResolution);
            int maxLocalLights = math.max(1, settings.maxLocalLights);
            Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
            ActiveCascadeCount = math.clamp(settings.cascadeCount, 1, CascadeCount);

            if (settings.shadowDistance <= 0)
            {
                return;
            }

            using (LightCpuMarkers.s_FitCascade.Auto())
            {
                AllocateCascade(records, cullingResults, camera, settings, cascadeRes);
            }
            using (LightCpuMarkers.s_AllocateLocal.Auto())
            {
                AllocateLocal(records, cullingResults, additionalData, camera, cameraPosition, settings, localRes, minTileResolution, maxTileResolution, maxLocalLights, frame, shadowCache);
            }
        }

        // Local shadow budget: candidates in descending score (record order on ties) are accepted while both the light
        // count and the atlas area budget allow. High-priority candidates step down by power-of-two tiers down to
        // minTileResolution under contention before any light is dropped.
        readonly struct ShadowBudgetComparer : System.Collections.Generic.IComparer<int>
        {
            readonly NativeArray<float> m_Scores;
            readonly NativeArray<int> m_Records;
            internal ShadowBudgetComparer(NativeArray<float> scores, NativeArray<int> records)
            {
                m_Scores = scores;
                m_Records = records;
            }
            public int Compare(int a, int b)
            {
                int cmp = m_Scores[b].CompareTo(m_Scores[a]);
                return cmp != 0 ? cmp : m_Records[a].CompareTo(m_Records[b]);
            }
        }

        readonly struct ShadowTierPackComparer : System.Collections.Generic.IComparer<int>
        {
            readonly NativeArray<int> m_Resolutions;
            readonly NativeArray<int> m_Indices;
            internal ShadowTierPackComparer(NativeArray<int> resolutions, NativeArray<int> indices)
            {
                m_Resolutions = resolutions;
                m_Indices = indices;
            }
            public int Compare(int a, int b)
            {
                int cmp = m_Resolutions[b].CompareTo(m_Resolutions[a]);
                return cmp != 0 ? cmp : m_Indices[a].CompareTo(m_Indices[b]);
            }
        }

        internal static int SelectWithinBudget(NativeArray<float> scores, NativeArray<int> recordIndices, NativeArray<int> faceCounts, int maxLocalLights, int tileBudget,
            NativeArray<int> acceptedRecords, int count, out int tilesUsed, out int dropped)
        {
            using var desired = new NativeArray<int>(math.max(1, count), Allocator.Temp);
            using var acceptedRes = new NativeArray<int>(math.max(1, acceptedRecords.Length), Allocator.Temp);
            var writableDesired = desired;
            for (int i = 0; i < count; ++i)
                writableDesired[i] = 1;
            int accepted = SelectWithinVariableBudget(scores, recordIndices, faceCounts, desired, maxLocalLights, tileBudget, 1,
                acceptedRecords, acceptedRes, count, out long texelsUsed, out _, out dropped);
            tilesUsed = (int)texelsUsed;
            return accepted;
        }

        internal static int SelectWithinVariableBudget(
            NativeArray<float> scores,
            NativeArray<int> recordIndices,
            NativeArray<int> faceCounts,
            NativeArray<int> desiredResolutions,
            int maxLocalLights,
            long totalAtlasTexels,
            int minTileResolution,
            NativeArray<int> acceptedRecords,
            NativeArray<int> acceptedResolutions,
            int count,
            out long texelsUsed,
            out int downgraded,
            out int dropped)
        {
            texelsUsed = 0;
            downgraded = 0;
            dropped = 0;
            if (count <= 0 || maxLocalLights <= 0 || totalAtlasTexels <= 0 || acceptedRecords.Length == 0)
                return 0;

            using var order = new NativeArray<int>(count, Allocator.Temp);
            var writableOrder = order;
            for (int i = 0; i < count; ++i)
                writableOrder[i] = i;
            order.Sort(new ShadowBudgetComparer(scores, recordIndices));

            long remaining = totalAtlasTexels;
            int accepted = 0;
            for (int o = 0; o < count; ++o)
            {
                int candidate = order[o];
                int faces = math.max(1, faceCounts[candidate]);
                if (accepted >= maxLocalLights || accepted >= acceptedRecords.Length)
                {
                    dropped++;
                    continue;
                }

                int desired = math.max(minTileResolution, desiredResolutions[candidate]);
                int assigned = desired;
                while (assigned > minTileResolution && (long)faces * assigned * assigned > remaining)
                    assigned >>= 1;

                long cost = (long)faces * assigned * assigned;
                if (cost > remaining)
                {
                    dropped++;
                    continue;
                }

                if (assigned < desired)
                    downgraded++;

                acceptedRecords[accepted] = recordIndices[candidate];
                acceptedResolutions[accepted] = assigned;
                accepted++;
                remaining -= cost;
                texelsUsed += cost;
            }
            return accepted;
        }

        static float ComputeProjectedPixels(in LightRecord record, Light light, Camera camera, float distance, int minTileResolution, int localRes)
        {
            float range = math.max(record.positionRange.w, 0.1f);
            float extent = range;
            int flags = record.flags;
            if ((flags & LightRecordFlags.SpotBox) != 0)
            {
                extent = math.max(0.25f, math.max(record.area.x, record.area.y));
            }
            else if ((flags & LightRecordFlags.SpotCone) != 0)
            {
                float cosOuter = Mathf.Clamp(record.forward.w, 0f, 0.9999f);
                float sinOuter = Mathf.Sqrt(Mathf.Max(0f, 1f - cosOuter * cosOuter));
                extent = math.max(0.25f, range * sinOuter);
            }
            else if ((flags & LightRecordFlags.SpotPyramid) != 0)
            {
                float tanMax = math.max(record.area.x, record.area.y);
                float sinApprox = tanMax / math.sqrt(1f + tanMax * tanMax);
                extent = math.max(0.25f, range * sinApprox);
            }
            else if ((flags & LightRecordFlags.Planar) != 0)
            {
                float emitterSize = record.area.x > 0 && record.area.y > 0 ? math.max(record.area.x, record.area.y) : record.shape.x * 2f;
                extent = math.max(emitterSize, range * 0.75f);
            }

            float screenHeight = camera != null ? math.max(1f, camera.pixelHeight) : 1080f;
            float projScale = 1.7320508f;
            float coverage = 1f;
            if (camera != null)
            {
                if (camera.orthographic)
                    coverage = extent / math.max(0.1f, camera.orthographicSize);
                else
                {
                    projScale = 1.0f / math.max(0.1f, math.tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
                    coverage = (extent / math.max(distance, 0.1f)) * (projScale * 0.5f);
                }
            }
            else
            {
                coverage = (extent / math.max(distance, 0.1f)) * (projScale * 0.5f);
            }
            return math.clamp(coverage * screenHeight, minTileResolution, localRes);
        }

        static readonly Plane[] s_CullingPlanes = new Plane[6];

        void AllocateLocal(
            NativeList<LightRecord> records,
            in CullingResults cullingResults,
            InfinityAdditionalLightData[] additionalData,
            Camera camera, Vector3 cameraPosition,
            in ShadowAllocatorSettings settings,
            int localRes,
            int minTileResolution,
            int maxTileResolution,
            int maxLocalLights, int frame, LocalShadowCache shadowCache)
        {
            // A cached record's sampling occupies up to six entries at recordIndex * MaxLocalFaces; cachedFaces holds their
            // count, 0 when the record is not served from the cache. Both live until this call returns (AGENTS R17).
            var cachedSampling = new NativeArray<LocalShadowCache.SamplingState>(math.max(1, records.Length * MaxLocalFaces), Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var cachedFaces = new NativeArray<int>(math.max(1, records.Length), Allocator.Temp);
            ulong sceneMask = CameraSceneCulling.Mask(camera);
            if (shadowCache != null)
                for (int i = 0; i < records.Length; ++i)
                {
                    var record = records[i];
                    if (record.IsDirectional || record.visibleLightIndex < 0)
                    {
                        continue;
                    }
                    Light light = cullingResults.visibleLights[record.visibleLightIndex].light;
                    InfinityAdditionalLightData ext = additionalData[record.visibleLightIndex];
                    if (light != null && light.shadows != LightShadows.None
                        && ext != null
                        && ext.shadowUpdateMode == EShadowUpdateMode.OnDemand)
                        cachedFaces[i] = shadowCache.ResolveSampling(UnityEntityId.ToUInt64(light), sceneMask, ext.shadowCacheVersion, frame,
                            cachedSampling.AsSpan().Slice(i * MaxLocalFaces, MaxLocalFaces));
                }

            int candidateCount = 0;
            for (int i = 0; i < records.Length; ++i)
            {
                if (cachedFaces[i] != 0 || IsLocalShadowCandidate(records[i], cullingResults, camera, minTileResolution, settings.filterGuardTexels))
                {
                    candidateCount++;
                }
            }

            var scores = new NativeArray<float>(math.max(1, candidateCount), Allocator.Temp);
            var recordIndices = new NativeArray<int>(math.max(1, candidateCount), Allocator.Temp);
            var desiredResolutions = new NativeArray<int>(math.max(1, candidateCount), Allocator.Temp);
            var faceCounts = new NativeArray<int>(math.max(1, candidateCount), Allocator.Temp);
            int write = 0;
            for (int i = 0; i < records.Length; ++i)
            {
                LightRecord record = records[i];
                if (cachedFaces[i] == 0 && !IsLocalShadowCandidate(record, cullingResults, camera, minTileResolution, settings.filterGuardTexels))
                {
                    continue;
                }

                VisibleLight visible = cullingResults.visibleLights[record.visibleLightIndex];
                Vector3 lightPosition = visible.localToWorldMatrix.GetColumn(3);
                float distance = math.max(0.01f, Vector3.Distance(cameraPosition, lightPosition));
                // Strength over distance; single-tile shapes win ties against six-face shapes.
                int faces = FaceCount(cachedFaces[i], record);
                float score = visible.light.shadowStrength / distance;
                if (faces == 1)
                {
                    score += 1e-4f;
                }

                int desiredRes;
                if (cachedFaces[i] != 0)
                {
                    LocalShadowCache.SamplingState cachedFirst = cachedSampling[i * MaxLocalFaces];
                    int rawCachedRes = cachedFirst.tileResolution > 0 ? cachedFirst.tileResolution : (int)cachedFirst.slice.atlasPixelRect.z;
                    int cachedTileRes = ShadowAtlasLayout.PreviousPowerOfTwo(math.max(minTileResolution, rawCachedRes));
                    desiredRes = ShadowResolutionHistory.CapResolutionForFaceCount(math.min(maxTileResolution, cachedTileRes), minTileResolution, localRes, faces);
                }
                else
                {
                    float projectedPixels = ComputeProjectedPixels(record, visible.light, camera, distance, minTileResolution, localRes);
                    desiredRes = m_ResolutionHistory.Resolve(UnityEntityId.ToUInt64(visible.light), sceneMask, projectedPixels, faces,
                        minTileResolution, maxTileResolution, localRes, frame);
                }

                scores[write] = score;
                recordIndices[write] = i;
                faceCounts[write] = faces;
                desiredResolutions[write] = desiredRes;
                write++;
            }

            int acceptedCapacity = math.max(1, math.min(maxLocalLights, write));
            var acceptedRecords = new NativeArray<int>(acceptedCapacity, Allocator.Temp);
            var acceptedResolutions = new NativeArray<int>(acceptedCapacity, Allocator.Temp);
            int accepted = SelectWithinVariableBudget(scores, recordIndices, faceCounts, desiredResolutions, maxLocalLights, m_FrameLayout.TotalTexels,
                minTileResolution, acceptedRecords, acceptedResolutions, write, out _, out int downgradedLights, out int droppedLights);
            LocalDowngradedLightCount = downgradedLights;
            LocalDroppedLightCount = droppedLights;

            // Pack descending by assigned tile resolution so power-of-two quadtree packing incurs zero fragmentation.
            using var packOrder = new NativeArray<int>(math.max(1, accepted), Allocator.Temp);
            var writablePackOrder = packOrder;
            for (int a = 0; a < accepted; ++a)
                writablePackOrder[a] = a;
            if (accepted > 1)
                packOrder.GetSubArray(0, accepted).Sort(new ShadowTierPackComparer(acceptedResolutions, acceptedRecords));

            int maxRequiredSlices = 0;
            for (int a = 0; a < accepted; ++a)
                maxRequiredSlices += FaceCount(cachedFaces[acceptedRecords[a]], records[acceptedRecords[a]]);
            EnsureLocalCapacity(math.max(1, maxRequiredSlices));

            LocalSliceCount = 0;
            int nextSlice = 0;
            float guard = math.max(0, settings.filterGuardTexels);
            for (int p = 0; p < accepted; ++p)
            {
                int a = packOrder[p];
                int recordIndex = acceptedRecords[a];
                int tileResolution = acceptedResolutions[a];
                LightRecord record = records[recordIndex];
                int faces = FaceCount(cachedFaces[recordIndex], record);
                int visibleIndex = record.visibleLightIndex;
                Light light = cullingResults.visibleLights[visibleIndex].light;

                // If an OnDemand light was downgraded below its cached production resolution, render fresh at the smaller resolution.
                LocalShadowCache.SamplingState firstCached = cachedSampling[recordIndex * MaxLocalFaces];
                int cachedProductionRes = firstCached.tileResolution > 0 ? firstCached.tileResolution : (int)firstCached.slice.atlasPixelRect.z;
                bool useCached = cachedFaces[recordIndex] != 0 && cachedProductionRes == tileResolution;
                if (!useCached && cachedFaces[recordIndex] != 0)
                {
                    faces = LightRecordPack.LocalShadowSliceCount(record);
                }

                // Allocate all faces cleanly (if a quadtree boundary prevents allocation, step down or roll back this light).
                bool allocatedAll = true;
                while (true)
                {
                    allocatedAll = true;
                    EnsureLocalCapacity(math.max(1, nextSlice + faces));
                    for (int face = 0; face < faces; ++face)
                    {
                        if (!m_FrameLayout.TryAllocate(tileResolution, out int handle, out Vector4 pixelRect))
                        {
                            for (int undo = 0; undo < face; ++undo)
                                m_FrameLayout.Free((int)LocalSlices[nextSlice + undo].matrixIndex);
                            allocatedAll = false;
                            break;
                        }
                        // Temporarily stash layout handle in matrixIndex until slices are populated below.
                        LocalSlices[nextSlice + face].matrixIndex = handle;
                        LocalSlices[nextSlice + face].atlasPixelRect = pixelRect;
                    }
                    if (allocatedAll || tileResolution <= minTileResolution)
                        break;
                    tileResolution >>= 1;
                    useCached = false;
                    faces = LightRecordPack.LocalShadowSliceCount(record);
                }
                if (!allocatedAll)
                {
                    LocalDroppedLightCount++;
                    continue;
                }

                int firstSlice = nextSlice;
                if (useCached)
                    shadowCache.PinSampling(UnityEntityId.ToUInt64(light), sceneMask, faces, frame);

                for (int face = 0; face < faces; ++face)
                {
                    int slice = nextSlice + face;
                    Vector4 pixelRect = LocalSlices[slice].atlasPixelRect;
                    LocalShadowSlice local = default;
                    local.visibleLightIndex = visibleIndex;
                    local.recordIndex = recordIndex;
                    local.face = face;
                    local.matrixIndex = slice;
                    local.isSpot = faces == 1;
                    local.shadowFilter = record.shadowFilter;
                    local.atlasPixelRect = pixelRect;
                    local.atlasUVRect = new Vector4(pixelRect.x / localRes, pixelRect.y / localRes, pixelRect.z / localRes, pixelRect.w / localRes);

                    LocalShadowViewProjection frustum = LocalShadowFrustum(record, light, face, tileResolution, guard);
                    local.projectionType = frustum.orthographic ? BatchCullingProjectionType.Orthographic : BatchCullingProjectionType.Perspective;
                    local.valid = frustum.valid;
                    local.shadowMatrix = Matrix4x4.identity;
                    var gpu = new LocalShadowSliceGPU
                    {
                        atlasRect = local.atlasUVRect,
                        depthParams = new Vector4(0, 1, 1, 0),
                        momentParams = new Vector4(1, 0, 0, 0),
                        emitterAxisU = new Vector4(1, 0, 0, ShadowPCSSShapeMode.IsotropicDisk),
                        emitterAxisV = new Vector4(0, 1, 0, 1)
                    };
                    if (frustum.valid)
                    {
                        Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(frustum.projection, true);
                        local.shadowMatrix = gpuProjection * frustum.view;
                        float texelSize = 2f / (Mathf.Abs(frustum.projection.m00) * tileResolution);
                        local.casterBias = new Vector4(-light.shadowBias * texelSize, -light.shadowNormalBias * texelSize, 0, 0);
                        local.splitData = CullingSplit(frustum.projection * frustum.view, frustum.near);
                        gpu.depthParams = new Vector4(gpuProjection.m22, gpuProjection.m23, texelSize, frustum.orthographic ? 1 : 0);
                        gpu.momentParams = new Vector4(1.0f / math.max(frustum.far, 1e-4f), frustum.apexOffset, 0, 0);
                        local.far = frustum.far;
                    }

                    PackEmitterShadowShape(record, frustum, out gpu.shadowOriginSize, out gpu.emitterAxisU, out gpu.emitterAxisV);
                    if (useCached)
                    {
                        var snapshot = cachedSampling[recordIndex * MaxLocalFaces + face];
                        local.cachedSampling = true;
                        // Keep this camera's atlas layout and light record identity; depth geometry is the production's.
                        Matrix4x4 matrix = snapshot.slice.shadowMatrix;
                        local.shadowMatrix = matrix;
                        local.casterBias = snapshot.slice.casterBias;
                        local.splitData = snapshot.slice.splitData;
                        local.projectionType = snapshot.slice.projectionType;
                        local.far = snapshot.slice.far;
                        local.valid = snapshot.slice.valid;
                        Vector4 atlasRect = gpu.atlasRect;
                        gpu = snapshot.gpu;
                        gpu.atlasRect = atlasRect;
                    }
                    if (local.valid && UsesMoments(local.shadowFilter))
                    {
                        LocalMomentSliceCount++;
                    }
                    if (tileResolution >= 1024)
                        LocalTileCount1024++;
                    else if (tileResolution >= 512)
                        LocalTileCount512++;
                    else if (tileResolution >= 256)
                        LocalTileCount256++;
                    else
                        LocalTileCount128OrSmaller++;

                    LocalSlices[slice] = local;
                    LocalMatrices[slice] = local.shadowMatrix;
                    LocalGPU[slice] = gpu;
                }

                record.shadowAtlasRect = LocalSlices[firstSlice].atlasUVRect;
                record.shadowMatrixIndex = firstSlice;
                record.shadowSliceCount = faces;
                record.flags |= LightRecordFlags.EnableShadow;
                records[recordIndex] = record;
                nextSlice += faces;
            }

            LocalSliceCount = nextSlice;
            LocalAtlasOccupancy = m_FrameLayout.Occupancy;
            scores.Dispose();
            recordIndices.Dispose();
            desiredResolutions.Dispose();
            faceCounts.Dispose();
            acceptedRecords.Dispose();
            acceptedResolutions.Dispose();
            cachedFaces.Dispose();
            cachedSampling.Dispose();
        }

        const int MaxLocalFaces = 6;

        static int FaceCount(int cachedFaceCount, in LightRecord record)
            => cachedFaceCount != 0 ? cachedFaceCount : LightRecordPack.LocalShadowSliceCount(record);

        bool IsLocalShadowCandidate(in LightRecord record, in CullingResults cullingResults, Camera camera, int tileResolution, float guardTexels)
        {
            if (record.IsDirectional || record.visibleLightIndex < 0 || LightRecordPack.LocalShadowSliceCount(record) <= 0)
            {
                return false;
            }
            Light light = cullingResults.visibleLights[record.visibleLightIndex].light;
            return light != null && light.shadows != LightShadows.None && (cullingResults.GetShadowCasterBounds(record.visibleLightIndex, out _)
                || m_MeshCasters.HasLocalCaster(record, light, camera, tileResolution, guardTexels));
        }

        internal struct LocalShadowViewProjection
        {
            public Matrix4x4 view;
            public Matrix4x4 projection;
            public float near;
            public float far;
            public float apexOffset;
            public bool orthographic;
            public bool valid;
        }

        // Projects emitter dimensions into this slice's view XY frame for shape-aware anisotropic PCSS.
        internal static void PackEmitterShadowShape(in LightRecord record, in LocalShadowViewProjection frustum,
            out Vector4 shadowOriginSize, out Vector4 emitterAxisU, out Vector4 emitterAxisV)
        {
            Vector3 origin = new Vector3(record.positionRange.x, record.positionRange.y, record.positionRange.z);
            float texelScaleY = frustum.valid
                ? Mathf.Abs(frustum.projection.m11) / Mathf.Max(Mathf.Abs(frustum.projection.m00), 1e-6f)
                : 1.0f;
            int flags = record.flags;
            bool isPlanar = (flags & LightRecordFlags.Planar) != 0;
            bool isSpot = (flags & (LightRecordFlags.SpotCone | LightRecordFlags.SpotPyramid | LightRecordFlags.SpotBox)) != 0;

            if (isPlanar && !isSpot && record.area.x > 0 && record.area.y > 0)
            {
                Vector3 worldForward = new Vector3(record.forward.x, record.forward.y, record.forward.z);
                Vector3 worldRight = new Vector3(record.right.x, record.right.y, record.right.z);
                Vector3 worldUp = Vector3.Cross(worldForward, worldRight);
                Vector3 viewRight = frustum.valid ? frustum.view.MultiplyVector(worldRight) : Vector3.right;
                Vector3 viewUp = frustum.valid ? frustum.view.MultiplyVector(worldUp) : Vector3.up;
                Vector2 axisU = new Vector2(viewRight.x, viewRight.y);
                Vector2 axisV = new Vector2(viewUp.x, viewUp.y);
                axisU = axisU.sqrMagnitude > 1e-8f ? axisU.normalized : Vector2.right;
                axisV = axisV.sqrMagnitude > 1e-8f ? axisV.normalized : Vector2.up;
                float halfWidth = 0.5f * record.area.x;
                float halfHeight = 0.5f * record.area.y;
                shadowOriginSize = new Vector4(origin.x, origin.y, origin.z, Mathf.Max(halfWidth, halfHeight));
                emitterAxisU = new Vector4(axisU.x, axisU.y, halfWidth, ShadowPCSSShapeMode.OrientedRect);
                emitterAxisV = new Vector4(axisV.x, axisV.y, halfHeight, texelScaleY);
                return;
            }

            if (!isPlanar && !isSpot && record.shape.z > 0)
            {
                Vector3 worldRight = new Vector3(record.right.x, record.right.y, record.right.z);
                Vector3 viewAxis = frustum.valid ? frustum.view.MultiplyVector(worldRight) : Vector3.right;
                float projectedScale = Mathf.Sqrt(viewAxis.x * viewAxis.x + viewAxis.y * viewAxis.y);
                Vector2 axisU = projectedScale > 1e-4f
                    ? new Vector2(viewAxis.x, viewAxis.y) / projectedScale
                    : Vector2.right;
                Vector2 axisV = new Vector2(-axisU.y, axisU.x);
                float halfLength = 0.5f * record.shape.z * projectedScale;
                float radius = Mathf.Max(0.0f, record.shape.x);
                shadowOriginSize = new Vector4(origin.x, origin.y, origin.z, Mathf.Max(radius, halfLength));
                emitterAxisU = new Vector4(axisU.x, axisU.y, halfLength, ShadowPCSSShapeMode.OrientedCapsule);
                emitterAxisV = new Vector4(axisV.x, axisV.y, radius, texelScaleY);
                return;
            }

            float isotropicRadius = Mathf.Max(0.0f, record.shape.x);
            shadowOriginSize = new Vector4(origin.x, origin.y, origin.z, isotropicRadius);
            emitterAxisU = new Vector4(1.0f, 0.0f, isotropicRadius, ShadowPCSSShapeMode.IsotropicDisk);
            emitterAxisV = new Vector4(0.0f, 1.0f, isotropicRadius, texelScaleY);
        }

        // Cube faces in SelectPointShadowFace order: +X, -X, +Y, -Y, +Z, -Z.
        static readonly Vector3[] s_FaceForward = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly Vector3[] s_FaceUp = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };

        // The view and projection of one local shadow tile, built from the record's shape flags.
        internal static LocalShadowViewProjection LocalShadowFrustum(in LightRecord record, Light light, int face, int tileResolution, float guardTexels)
        {
            var frustum = new LocalShadowViewProjection();
            Vector3 position = new Vector3(record.positionRange.x, record.positionRange.y, record.positionRange.z);
            float range = math.max(record.positionRange.w, 0.02f);
            float near = math.clamp(light != null ? light.shadowNearPlane : 0.1f, 0.01f, range * 0.5f);
            Vector3 forward = new Vector3(record.forward.x, record.forward.y, record.forward.z);
            Vector3 right = new Vector3(record.right.x, record.right.y, record.right.z);
            Vector3 up = Vector3.Cross(forward, right);
            // Widen each frustum by the filter footprint so tents and PCSS kernels stay inside the rendered area.
            float guardScale = (tileResolution + 2.0f * guardTexels) / math.max(1.0f, tileResolution);
            int flags = record.flags;

            if ((flags & (LightRecordFlags.SpotCone | LightRecordFlags.SpotPyramid | LightRecordFlags.SpotBox | LightRecordFlags.Planar)) == 0)
            {
                float fov = 2.0f * Mathf.Atan(guardScale) * Mathf.Rad2Deg;
                frustum.view = WorldToView(position, s_FaceForward[face], s_FaceUp[face]);
                frustum.projection = Matrix4x4.Perspective(fov, 1, near, range);
            }
            else if ((flags & LightRecordFlags.SpotCone) != 0)
            {
                float cosOuter = record.forward.w;
                float halfTan = (cosOuter > 0.0f && cosOuter < 1.0f
                    ? Mathf.Sqrt(1.0f - cosOuter * cosOuter) / cosOuter
                    : Mathf.Tan(Mathf.Clamp(light != null ? light.spotAngle : 60, 1, 179) * 0.5f * Mathf.Deg2Rad)) * guardScale;
                float apexOffset = (flags & LightRecordFlags.Planar) != 0 ? math.max(record.shape.y, 0.01f) : 0.0f;
                if (apexOffset > 0.0f)
                {
                    near = apexOffset;
                    frustum.far = range + apexOffset;
                }
                frustum.apexOffset = apexOffset;
                frustum.view = WorldToView(position - forward * apexOffset, forward, up);
                frustum.projection = Matrix4x4.Perspective(2.0f * Mathf.Atan(halfTan) * Mathf.Rad2Deg, 1, near, range + apexOffset);
            }
            else if ((flags & LightRecordFlags.SpotPyramid) != 0)
            {
                float tanX = record.area.x * guardScale, tanY = record.area.y * guardScale;
                frustum.view = WorldToView(position, forward, up);
                frustum.projection = Matrix4x4.Perspective(2.0f * Mathf.Atan(tanY) * Mathf.Rad2Deg, tanX / math.max(tanY, 1e-4f), near, range);
            }
            else if ((flags & LightRecordFlags.SpotBox) != 0)
            {
                float halfW = 0.5f * record.area.x * guardScale, halfH = 0.5f * record.area.y * guardScale;
                frustum.view = WorldToView(position, forward, up);
                frustum.projection = Matrix4x4.Ortho(-halfW, halfW, -halfH, halfH, 0.0f, range);
                frustum.orthographic = true;
                near = 0.0f;
            }
            else
            {
                // Rect or disk: a perspective frustum whose apex sits behind the emitter, so the emitter fills the near
                // plane of a PlanarShadowFieldOfView cone.
                float halfExtent = record.area.x > 0 && record.area.y > 0
                    ? 0.5f * Mathf.Sqrt(record.area.x * record.area.x + record.area.y * record.area.y)
                    : record.shape.x;
                float halfTan = Mathf.Tan(PlanarShadowFieldOfView * 0.5f * Mathf.Deg2Rad);
                float apexOffset = math.max(halfExtent / halfTan, 0.01f);
                frustum.apexOffset = apexOffset;
                frustum.view = WorldToView(position - forward * apexOffset, forward, up);
                near = apexOffset;
                frustum.projection = Matrix4x4.Perspective(2.0f * Mathf.Atan(halfTan * guardScale) * Mathf.Rad2Deg, 1, near, range + apexOffset);
                frustum.far = range + apexOffset;
            }

            if (frustum.far <= 0)
            {
                frustum.far = range;
            }
            frustum.near = near;
            frustum.valid = true;
            return frustum;
        }

        // Unity view space looks down -Z.
        static Matrix4x4 WorldToView(Vector3 position, Vector3 forward, Vector3 up)
        {
            Matrix4x4 lookAt = Matrix4x4.TRS(position, Quaternion.LookRotation(forward, up), new Vector3(1, 1, -1));
            return lookAt.inverse;
        }

        static ShadowSplitData CullingSplit(Matrix4x4 cullingMatrix, float near)
        {
            var split = new ShadowSplitData();
            Plane[] planes = s_CullingPlanes;
            GeometryUtility.CalculateFrustumPlanes(cullingMatrix, planes);
            split.cullingPlaneCount = planes.Length;
            for (int i = 0; i < planes.Length; ++i)
            {
                split.SetCullingPlane(i, planes[i]);
            }
            split.cullingMatrix = cullingMatrix;
            split.cullingNearPlane = near;
            return split;
        }

        static Matrix4x4 MakeShadowMatrix(Matrix4x4 projection, Matrix4x4 view) => GL.GetGPUProjectionMatrix(projection, true) * view;

        void EnsureLocalCapacity(int sliceCount)
        {
            if (LocalSlices.Length < sliceCount)
            {
                LocalSlices = new LocalShadowSlice[sliceCount];
                LocalMatrices = new Matrix4x4[sliceCount];
                LocalGPU = new LocalShadowSliceGPU[sliceCount];
            }
        }
    }
}
