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
    internal struct FShadowAllocatorSettings
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

    internal struct FLocalShadowSlice
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

    // Per local shadow slice data the shaders read (SRV_LocalShadowRects).
    // GPU-RECORD: FLocalShadowSlice Shaders/ShaderLibrary/ShadowSampling.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct FLocalShadowSliceGPU
    {
        // xy = atlas UV origin, zw = atlas UV size.
        public Vector4 atlasRect;
        // x, y = m22, m23 of the GPU projection (linear depth from stored depth); z = world texel size at unit depth
        // (perspective) or world texel size (orthographic); w = 1 for orthographic.
        public Vector4 depthParams;
        // x = 1 / far: normalizes linear depth for VSM / EVSM moments.
        public Vector4 momentParams;
        // xyz = origin used to select cube faces; w = PCSS emitter radius of the stored depth production.
        public Vector4 shadowOriginSize;
    }

    internal struct FCascadeShadowSlice
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
    /// planes go to CullShadowCasters. Writes atlas / matrix indices back into FLightRecord before the GPU upload.
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
        public int CascadeAllocatedCount { get; private set; }
        // Cascades requested by the asset (1-4); slices past it stay invalid.
        public int ActiveCascadeCount { get; private set; } = CascadeCount;
        public Vector4 CascadeSplitDistances { get; private set; }
        public readonly FCascadeShadowSlice[] CascadeSlices = new FCascadeShadowSlice[CascadeCount];
        public readonly Matrix4x4[] CascadeMatrices = new Matrix4x4[CascadeCount];
        public readonly Vector4[] CascadeSpheres = new Vector4[CascadeCount];
        // x = world texel size, y, z = m22, m23 of the GPU projection, w = blend width (fraction of the cascade).
        public readonly Vector4[] CascadeParams = new Vector4[CascadeCount];

        // Shadow filter of the cascaded light (EShadowFilter), -1 without one.
        public int CascadeShadowFilter { get; private set; } = -1;
        public bool CascadeUsesMoments => CascadeAllocatedCount > 0 && UsesMoments(CascadeShadowFilter);
        // Valid local slices whose light filters with VSM / EVSM.
        public int LocalMomentSliceCount { get; private set; }

        public static bool UsesMoments(int shadowFilter) => shadowFilter == (int)EShadowFilter.VSM || shadowFilter == (int)EShadowFilter.EVSM;

        public int LocalSliceCount { get; private set; }
        public FLocalShadowSlice[] LocalSlices = Array.Empty<FLocalShadowSlice>();
        public Matrix4x4[] LocalMatrices = Array.Empty<Matrix4x4>();
        public FLocalShadowSliceGPU[] LocalGPU = Array.Empty<FLocalShadowSliceGPU>();

        public void Reset()
        {
            CascadeVisibleLightIndex = -1;
            CascadeRecordIndex = -1;
            CascadeAllocatedCount = 0;
            CascadeSplitDistances = Vector4.zero;
            CascadeShadowFilter = -1;
            LocalSliceCount = 0;
            LocalMomentSliceCount = 0;
            for (int i = 0; i < CascadeCount; ++i)
            {
                CascadeSlices[i] = default;
                CascadeMatrices[i] = Matrix4x4.identity;
                CascadeSpheres[i] = Vector4.zero;
                CascadeParams[i] = Vector4.zero;
            }
        }

        public void Allocate(
            NativeList<FLightRecord> records,
            in CullingResults cullingResults,
            Camera camera,
            in FShadowAllocatorSettings settings, MeshWorld meshWorld, LocalShadowCache shadowCache = null)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (meshWorld == null) throw new ArgumentNullException(nameof(meshWorld));
            m_MeshCasters.Collect(meshWorld);
            Reset();

            int cascadeRes = math.max(1, settings.cascadeMapResolution);
            int localRes = math.max(1, settings.localMapResolution);
            int tileResolution = math.max(1, localRes / 4);
            int tilesPerRow = math.max(1, localRes / tileResolution);
            int tileBudget = tilesPerRow * tilesPerRow;
            int maxLocalLights = math.max(1, settings.maxLocalLights);
            Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
            ActiveCascadeCount = math.clamp(settings.cascadeCount, 1, CascadeCount);

            if (settings.shadowDistance <= 0) return;

            AllocateCascade(records, cullingResults, camera, settings, cascadeRes);
            AllocateLocal(records, cullingResults, camera, cameraPosition, settings, localRes, tileResolution, tilesPerRow, tileBudget, maxLocalLights, shadowCache);
        }

        // Local shadow budget: candidates in descending score (record order on ties) are accepted while both the light
        // count and the atlas tile budget allow; one that does not fit is dropped and a later, smaller one may still fit.
        // Writes the accepted record indices in acceptance order and returns their count.
        internal static int SelectWithinBudget(float[] scores, int[] recordIndices, int[] faceCounts, int maxLocalLights, int tileBudget,
            int[] acceptedRecords, out int tilesUsed, out int dropped)
        {
            return SelectWithinBudget(scores, recordIndices, faceCounts, maxLocalLights, tileBudget, acceptedRecords, scores.Length, out tilesUsed, out dropped);
        }
        readonly struct ShadowBudgetComparer : System.Collections.Generic.IComparer<int>
        {
            readonly float[] m_Scores; readonly int[] m_Records;
            internal ShadowBudgetComparer(float[] scores, int[] records) { m_Scores = scores; m_Records = records; }
            public int Compare(int a, int b)
            {
                int cmp = m_Scores[b].CompareTo(m_Scores[a]);
                return cmp != 0 ? cmp : m_Records[a].CompareTo(m_Records[b]);
            }
        }
        static int SelectWithinBudget(float[] scores, int[] recordIndices, int[] faceCounts, int maxLocalLights, int tileBudget,
            int[] acceptedRecords, int count, out int tilesUsed, out int dropped)
        {
            using var order = new NativeArray<int>(count, Allocator.Temp);
            var writableOrder = order;
            for (int i = 0; i < count; ++i) writableOrder[i] = i;
            order.Sort(new ShadowBudgetComparer(scores, recordIndices));

            tilesUsed = 0;
            dropped = 0;
            int accepted = 0;
            for (int o = 0; o < count; ++o)
            {
                int candidate = order[o];
                int faces = faceCounts[candidate];
                if (accepted >= maxLocalLights || accepted >= acceptedRecords.Length || tilesUsed + faces > tileBudget)
                {
                    dropped++;
                    continue;
                }

                acceptedRecords[accepted++] = recordIndices[candidate];
                tilesUsed += faces;
            }
            return accepted;
        }

        LocalShadowCache.SamplingState[][] m_CachedSampling = Array.Empty<LocalShadowCache.SamplingState[]>();
        int[] m_FaceCounts = Array.Empty<int>(), m_CandidateRecords = Array.Empty<int>(), m_AcceptedRecords = Array.Empty<int>();
        float[] m_CandidateScores = Array.Empty<float>();
        static T[] BudgetStorage<T>(ref T[] values, int count)
        {
            if (values.Length < count) Array.Resize(ref values, math.ceilpow2(math.max(1, count)));
            return values;
        }
        static readonly Plane[] s_CullingPlanes = new Plane[6];

        void AllocateLocal(
            NativeList<FLightRecord> records,
            in CullingResults cullingResults,
            Camera camera, Vector3 cameraPosition,
            in FShadowAllocatorSettings settings,
            int localRes,
            int tileResolution,
            int tilesPerRow,
            int tileBudget,
            int maxLocalLights, LocalShadowCache shadowCache)
        {
            var cached = BudgetStorage(ref m_CachedSampling, records.Length);
            Array.Clear(cached, 0, cached.Length);
            if (shadowCache != null)
                for (int i = 0; i < records.Length; ++i)
                {
                    var record = records[i];
                    if (record.IsDirectional || record.visibleLightIndex < 0) continue;
                    Light light = cullingResults.visibleLights[record.visibleLightIndex].light;
                    if (light != null && light.shadows != LightShadows.None
                        && light.TryGetComponent(out InfinityAdditionalLightData ext)
                        && ext.shadowUpdateMode == EShadowUpdateMode.OnDemand)
                        cached[i] = shadowCache.ResolveSampling(UnityEntityId.ToUInt64(light), MeshView.SceneCullingMaskFor(camera), ext.shadowCacheVersion, Time.frameCount);
                }
            int candidateCount = 0;
            for (int i = 0; i < records.Length; ++i)
            {
                if (cached[i] != null || IsLocalShadowCandidate(records[i], cullingResults, camera, tileResolution, settings.filterGuardTexels))
                {
                    candidateCount++;
                }
            }

            var scores = new NativeArray<float>(math.max(1, candidateCount), Allocator.Temp);
            var recordIndices = new NativeArray<int>(math.max(1, candidateCount), Allocator.Temp);
            int write = 0;
            for (int i = 0; i < records.Length; ++i)
            {
                FLightRecord record = records[i];
                if (cached[i] == null && !IsLocalShadowCandidate(record, cullingResults, camera, tileResolution, settings.filterGuardTexels))
                {
                    continue;
                }

                VisibleLight visible = cullingResults.visibleLights[record.visibleLightIndex];
                Vector3 lightPosition = visible.localToWorldMatrix.GetColumn(3);
                float distance = math.max(0.01f, Vector3.Distance(cameraPosition, lightPosition));
                // Strength over distance; single-tile shapes win ties against six-face shapes.
                float score = visible.light.shadowStrength / distance;
                if ((cached[i]?.Length ?? FLightRecordPack.LocalShadowSliceCount(record)) == 1)
                {
                    score += 1e-4f;
                }

                scores[write] = score;
                recordIndices[write] = i;
                write++;
            }

            var faceCounts = BudgetStorage(ref m_FaceCounts, write);
            var candidateScores = BudgetStorage(ref m_CandidateScores, write);
            var candidateRecords = BudgetStorage(ref m_CandidateRecords, write);
            for (int c = 0; c < write; ++c)
            {
                candidateScores[c] = scores[c];
                candidateRecords[c] = recordIndices[c];
                faceCounts[c] = cached[recordIndices[c]]?.Length ?? FLightRecordPack.LocalShadowSliceCount(records[recordIndices[c]]);
            }

            var acceptedRecords = BudgetStorage(ref m_AcceptedRecords, math.min(maxLocalLights, write));
            int accepted = SelectWithinBudget(candidateScores, candidateRecords, faceCounts, maxLocalLights, tileBudget, acceptedRecords, write, out int tilesUsed, out int dropped);
            MeshPipelineDiagnostics.LocalShadowBudgetDropped += dropped;

            EnsureLocalCapacity(math.max(1, tilesUsed));
            LocalSliceCount = 0;
            int nextSlice = 0;
            float guard = math.max(0, settings.filterGuardTexels);
            for (int a = 0; a < accepted; ++a)
            {
                int recordIndex = acceptedRecords[a];
                FLightRecord record = records[recordIndex];
                int faces = cached[recordIndex]?.Length ?? FLightRecordPack.LocalShadowSliceCount(record);
                int visibleIndex = record.visibleLightIndex;
                Light light = cullingResults.visibleLights[visibleIndex].light;
                int firstSlice = nextSlice;
                if (cached[recordIndex] != null)
                    shadowCache.PinSampling(UnityEntityId.ToUInt64(light), MeshView.SceneCullingMaskFor(camera), faces, Time.frameCount);

                for (int face = 0; face < faces; ++face)
                {
                    int slice = nextSlice + face;
                    int col = slice % tilesPerRow;
                    int row = slice / tilesPerRow;
                    float x = col * tileResolution;
                    float y = row * tileResolution;
                    FLocalShadowSlice local = default;
                    local.visibleLightIndex = visibleIndex;
                    local.recordIndex = recordIndex;
                    local.face = face;
                    local.matrixIndex = slice;
                    local.isSpot = faces == 1;
                    local.shadowFilter = record.shadowFilter;
                    local.atlasPixelRect = new Vector4(x, y, tileResolution, tileResolution);
                    local.atlasUVRect = new Vector4(x / localRes, y / localRes, (float)tileResolution / localRes, (float)tileResolution / localRes);

                    FLocalShadowFrustum frustum = LocalShadowFrustum(record, light, face, tileResolution, guard);
                    local.projectionType = frustum.orthographic ? BatchCullingProjectionType.Orthographic : BatchCullingProjectionType.Perspective;
                    local.valid = frustum.valid;
                    local.shadowMatrix = Matrix4x4.identity;
                    var gpu = new FLocalShadowSliceGPU { atlasRect = local.atlasUVRect, depthParams = new Vector4(0, 1, 1, 0), momentParams = new Vector4(1, 0, 0, 0) };
                    if (frustum.valid)
                    {
                        Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(frustum.projection, true);
                        local.shadowMatrix = gpuProjection * frustum.view;
                        float texelSize = 2f / (Mathf.Abs(frustum.projection.m00) * tileResolution);
                        local.casterBias = new Vector4(-light.shadowBias * texelSize, -light.shadowNormalBias * texelSize, 0, 0);
                        local.splitData = CullingSplit(frustum.projection * frustum.view, frustum.near);
                        gpu.depthParams = new Vector4(gpuProjection.m22, gpuProjection.m23, texelSize, frustum.orthographic ? 1 : 0);
                        gpu.momentParams = new Vector4(1.0f / math.max(frustum.far, 1e-4f), 0, 0, 0);
                        local.far = frustum.far;
                    }

                    gpu.shadowOriginSize = new Vector4(record.positionRange.x, record.positionRange.y, record.positionRange.z,
                        (record.flags & FLightRecordFlags.Planar) != 0 && record.area.x > 0 && record.area.y > 0
                            ? math.sqrt(record.area.x * record.area.y / math.PI) : record.shape.x + record.shape.y);
                    if (cached[recordIndex] != null)
                    {
                        var snapshot = cached[recordIndex][face];
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
                    if (local.valid && UsesMoments(local.shadowFilter)) LocalMomentSliceCount++;
                    LocalSlices[slice] = local;
                    LocalMatrices[slice] = local.shadowMatrix;
                    LocalGPU[slice] = gpu;
                }

                record.shadowAtlasRect = LocalSlices[firstSlice].atlasUVRect;
                record.shadowMatrixIndex = firstSlice;
                record.shadowSliceCount = faces;
                record.flags |= FLightRecordFlags.EnableShadow;
                records[recordIndex] = record;
                nextSlice += faces;
            }

            LocalSliceCount = nextSlice;
            scores.Dispose();
            recordIndices.Dispose();
        }

        bool IsLocalShadowCandidate(in FLightRecord record, in CullingResults cullingResults, Camera camera, int tileResolution, float guardTexels)
        {
            if (record.IsDirectional || record.visibleLightIndex < 0 || FLightRecordPack.LocalShadowSliceCount(record) <= 0)
            {
                return false;
            }
            Light light = cullingResults.visibleLights[record.visibleLightIndex].light;
            return light != null && light.shadows != LightShadows.None && (cullingResults.GetShadowCasterBounds(record.visibleLightIndex, out _)
                || m_MeshCasters.HasLocalCaster(record, light, camera, tileResolution, guardTexels));
        }

        internal struct FLocalShadowFrustum
        {
            public Matrix4x4 view;
            public Matrix4x4 projection;
            public float near;
            public float far;
            public bool orthographic;
            public bool valid;
        }

        // Cube faces in SelectPointShadowFace order: +X, -X, +Y, -Y, +Z, -Z.
        static readonly Vector3[] s_FaceForward = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly Vector3[] s_FaceUp = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };

        // The view and projection of one local shadow tile, built from the record's shape flags.
        internal static FLocalShadowFrustum LocalShadowFrustum(in FLightRecord record, Light light, int face, int tileResolution, float guardTexels)
        {
            var frustum = new FLocalShadowFrustum();
            Vector3 position = new Vector3(record.positionRange.x, record.positionRange.y, record.positionRange.z);
            float range = math.max(record.positionRange.w, 0.02f);
            float near = math.clamp(light != null ? light.shadowNearPlane : 0.1f, 0.01f, range * 0.5f);
            Vector3 forward = new Vector3(record.forward.x, record.forward.y, record.forward.z);
            Vector3 right = new Vector3(record.right.x, record.right.y, record.right.z);
            Vector3 up = Vector3.Cross(forward, right);
            // Widen each frustum by the filter footprint so tents and PCSS kernels stay inside the rendered area.
            float guardScale = (tileResolution + 2.0f * guardTexels) / math.max(1.0f, tileResolution);
            int flags = record.flags;

            if ((flags & (FLightRecordFlags.SpotCone | FLightRecordFlags.SpotPyramid | FLightRecordFlags.SpotBox | FLightRecordFlags.Planar)) == 0)
            {
                float fov = 2.0f * Mathf.Atan(guardScale) * Mathf.Rad2Deg;
                frustum.view = WorldToView(position, s_FaceForward[face], s_FaceUp[face]);
                frustum.projection = Matrix4x4.Perspective(fov, 1, near, range);
            }
            else if ((flags & FLightRecordFlags.SpotCone) != 0)
            {
                float halfTan = Mathf.Tan(Mathf.Clamp(light != null ? light.spotAngle : 60, 1, 179) * 0.5f * Mathf.Deg2Rad) * guardScale;
                frustum.view = WorldToView(position, forward, up);
                frustum.projection = Matrix4x4.Perspective(2.0f * Mathf.Atan(halfTan) * Mathf.Rad2Deg, 1, near, range);
            }
            else if ((flags & FLightRecordFlags.SpotPyramid) != 0)
            {
                float tanX = record.area.x * guardScale, tanY = record.area.y * guardScale;
                frustum.view = WorldToView(position, forward, up);
                frustum.projection = Matrix4x4.Perspective(2.0f * Mathf.Atan(tanY) * Mathf.Rad2Deg, tanX / math.max(tanY, 1e-4f), near, range);
            }
            else if ((flags & FLightRecordFlags.SpotBox) != 0)
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
                frustum.view = WorldToView(position - forward * apexOffset, forward, up);
                near = apexOffset;
                frustum.projection = Matrix4x4.Perspective(2.0f * Mathf.Atan(halfTan * guardScale) * Mathf.Rad2Deg, 1, near, range + apexOffset);
                frustum.far = range + apexOffset;
            }

            if (frustum.far <= 0) frustum.far = range;
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
                LocalSlices = new FLocalShadowSlice[sliceCount];
                LocalMatrices = new Matrix4x4[sliceCount];
                LocalGPU = new FLocalShadowSliceGPU[sliceCount];
            }
        }
    }
}
